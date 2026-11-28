package main

import (
	"bufio"
	"context"
	"crypto/rand"
	"encoding/hex"
	"encoding/json"
	"errors"
	"io"
	"log"
	"net/http"
	"net/url"
	"os"
	"os/exec"
	"strings"
	"sync"
	"time"

	"github.com/coder/websocket"
	"github.com/win30221/kingdom-rhapsody/server/internal/gamenetkit/transport"
)

type workerCatalog struct {
	Version int
	Cards   []string
}

type loadout struct {
	Left  *int     `json:"left"`
	Right *int     `json:"right"`
	Deck  []string `json:"deck"`
}

type request struct {
	Type    string   `json:"type"`
	Match   string   `json:"match,omitempty"`
	Card    string   `json:"card,omitempty"`
	Version int      `json:"version,omitempty"`
	Seat    int      `json:"seat"`
	Seq     int      `json:"seq,omitempty"`
	Slot    int      `json:"slot,omitempty"`
	X       float32  `json:"x,omitempty"`
	Y       float32  `json:"y,omitempty"`
	DX      float32  `json:"dx,omitempty"`
	DY      float32  `json:"dy,omitempty"`
	Loadout *loadout `json:"loadout,omitempty"`
}

type notice struct {
	Type string `json:"type"`
	Code string `json:"code,omitempty"`
}

type peer struct {
	ctx    context.Context
	close  func()
	wire   transport.BinaryWire
	out    chan []byte
	place  chan assignment
	config *loadout
}

type assignment struct {
	room *room
	seat int
}

func (client *peer) send(data []byte) {
	select {
	case <-client.ctx.Done():
	case client.out <- data:
	default:
		client.close()
	}
}

func (client *peer) writer() {
	for {
		select {
		case <-client.ctx.Done():
			return
		case data := <-client.out:
			deadline, stop := context.WithTimeout(client.ctx, 4*time.Second)
			err := client.wire.Write(deadline, data)
			stop()
			if err != nil {
				client.close()
				return
			}
		}
	}
}

type gateway struct {
	ctx                    context.Context
	dotnet, worker, actors string
	version                int
	cards                  map[string]bool
	capacity, connections  chan struct{}
	origins                []string
	originHosts            []string
	mu                     sync.Mutex
	waiting                *peer
}

func newGateway(ctx context.Context, dotnet, worker, actors string, catalog workerCatalog, capacity int) *gateway {
	app := &gateway{
		ctx: ctx, dotnet: dotnet, worker: worker, actors: actors, version: catalog.Version,
		cards: make(map[string]bool), capacity: make(chan struct{}, capacity), connections: make(chan struct{}, capacity*2+8),
	}
	for _, card := range catalog.Cards {
		app.cards[card] = true
	}
	return app
}

func (app *gateway) routes(browser, web string, origins []string) (http.Handler, error) {
	for _, origin := range origins {
		origin = strings.TrimSpace(origin)
		if origin == "" {
			continue
		}
		u, err := url.Parse(origin)
		if err != nil || (u.Scheme != "https" && u.Scheme != "http") || u.Host == "" ||
			u.User != nil || (u.Path != "" && u.Path != "/") || u.RawQuery != "" || u.Fragment != "" ||
			strings.ContainsAny(u.Host, "*?[]") {
			return nil, errors.New("each -origins item must be an exact HTTP(S) origin")
		}
		app.origins = append(app.origins, u.Scheme+"://"+u.Host)
		app.originHosts = append(app.originHosts, u.Host)
	}
	mux := http.NewServeMux()
	mux.HandleFunc("GET /healthz", func(w http.ResponseWriter, _ *http.Request) {
		_, _ = io.WriteString(w, "ok\n")
	})
	mux.HandleFunc("GET /ws", app.websocket)
	mux.HandleFunc("GET /transport.js", func(w http.ResponseWriter, r *http.Request) {
		for _, origin := range app.origins {
			if r.Header.Get("Origin") == origin {
				w.Header().Set("Access-Control-Allow-Origin", origin)
				break
			}
		}
		w.Header().Set("Vary", "Origin")
		w.Header().Set("Content-Type", "text/javascript; charset=utf-8")
		http.ServeFile(w, r, browser)
	})
	if web != "" {
		files := http.FileServer(http.Dir(web))
		mux.HandleFunc("GET /", func(w http.ResponseWriter, r *http.Request) {
			asset := r.URL.Path
			switch {
			case strings.HasSuffix(asset, ".gz"):
				w.Header().Set("Content-Encoding", "gzip")
				asset = strings.TrimSuffix(asset, ".gz")
			case strings.HasSuffix(asset, ".br"):
				w.Header().Set("Content-Encoding", "br")
				asset = strings.TrimSuffix(asset, ".br")
			}
			if strings.HasSuffix(asset, ".wasm") {
				w.Header().Set("Content-Type", "application/wasm")
			} else if strings.HasSuffix(asset, ".js") {
				w.Header().Set("Content-Type", "text/javascript; charset=utf-8")
			}
			files.ServeHTTP(w, r)
		})
	}
	return mux, nil
}

func (app *gateway) valid(config *loadout) bool {
	if config == nil || config.Left == nil || config.Right == nil || *config.Left < 0 || *config.Left > 1 ||
		*config.Right < 0 || *config.Right > 1 || len(config.Deck) != 8 {
		return false
	}
	seen := make(map[string]bool, 8)
	for _, id := range config.Deck {
		if !app.cards[id] || seen[id] {
			return false
		}
		seen[id] = true
	}
	return true
}

func (app *gateway) websocket(w http.ResponseWriter, r *http.Request) {
	select {
	case app.connections <- struct{}{}:
		defer func() { <-app.connections }()
	default:
		http.Error(w, "servers busy", http.StatusServiceUnavailable)
		return
	}
	conn, err := websocket.Accept(w, r, &websocket.AcceptOptions{OriginPatterns: app.originHosts, CompressionMode: websocket.CompressionContextTakeover})
	if err != nil {
		return
	}
	ctx, cancel := context.WithCancel(app.ctx)
	wire := transport.NewBinaryWebSocket(conn, transport.DefaultBinaryLimit)
	client := &peer{
		ctx: ctx, close: func() { cancel(); wire.Close() }, wire: wire,
		out: make(chan []byte, 48), place: make(chan assignment, 1),
	}
	defer client.close()
	defer app.remove(client)
	helloCtx, stop := context.WithTimeout(ctx, 8*time.Second)
	data, err := client.wire.Read(helloCtx)
	stop()
	if err != nil {
		return
	}
	var hello request
	if len(data) > 4096 || json.Unmarshal(data, &hello) != nil ||
		hello.Type != "queue" || !app.valid(hello.Loadout) {
		sendError(client, "invalid_loadout")
		return
	}
	if hello.Version != app.version {
		sendError(client, "version_mismatch")
		return
	}
	client.config = hello.Loadout
	go client.writer()
	client.send(jsonBytes(notice{Type: "waiting"}))
	if !app.join(client) {
		// The writer may close after it has delivered this terminal status.
		client.send(jsonBytes(notice{Type: "error", Code: "server_busy"}))
	}
	app.readCommands(client)
}

func sendError(client *peer, code string) {
	ctx, stop := context.WithTimeout(client.ctx, time.Second)
	defer stop()
	_ = client.wire.Write(ctx, jsonBytes(notice{Type: "error", Code: code}))
}

func (app *gateway) join(client *peer) bool {
	app.mu.Lock()
	defer app.mu.Unlock()
	if app.waiting == nil || app.waiting.ctx.Err() != nil {
		app.waiting = client
		return true
	}
	select {
	case app.capacity <- struct{}{}:
	default:
		return false
	}
	var seed [16]byte
	if _, err := rand.Read(seed[:]); err != nil {
		<-app.capacity
		return false
	}
	game := &room{
		id: hex.EncodeToString(seed[:]), peers: [2]*peer{app.waiting, client},
		commands: make(chan request, 64), done: make(chan struct{}),
	}
	app.waiting = nil
	for seat, player := range game.peers {
		player.place <- assignment{room: game, seat: seat}
	}
	go app.runRoom(game)
	return true
}

func (app *gateway) remove(client *peer) {
	app.mu.Lock()
	defer app.mu.Unlock()
	if app.waiting == client {
		app.waiting = nil
	}
}

func (app *gateway) readCommands(client *peer) {
	var place assignment
	lastSequence := 0
	for {
		bytes, err := client.wire.Read(client.ctx)
		if err != nil {
			return
		}
		var action request
		if len(bytes) > 1024 || json.Unmarshal(bytes, &action) != nil {
			return
		}
		if place.room == nil {
			select {
			case place = <-client.place:
			default:
				continue
			}
		}
		if action.Match != place.room.id || action.Seq <= lastSequence ||
			(action.Type != "play" && action.Type != "sprint") {
			continue
		}
		if action.Type == "play" && (!app.cards[action.Card] || action.Slot < 0 || action.Slot > 3) {
			return
		}
		lastSequence = action.Seq
		// The socket assignment is the only source of player identity.
		action.Seat = place.seat
		action.Loadout = nil
		select {
		case <-place.room.done:
		case place.room.commands <- action:
		default:
			return
		}
	}
}

type room struct {
	id       string
	peers    [2]*peer
	commands chan request
	done     chan struct{}
}

type workerMessage struct {
	Type  string
	Match string
	To    int
	State *struct{ Winner int }
}

func (app *gateway) runRoom(game *room) {
	defer func() {
		close(game.done)
		<-app.capacity
	}()
	ctx, cancel := context.WithTimeout(app.ctx, 265*time.Second)
	defer cancel()
	cmd := exec.CommandContext(ctx, app.dotnet, app.worker, app.actors)
	in, err := cmd.StdinPipe()
	if err != nil {
		game.fail()
		return
	}
	out, err := cmd.StdoutPipe()
	if err != nil {
		_ = in.Close()
		game.fail()
		return
	}
	cmd.Stderr = os.Stderr
	if err = cmd.Start(); err != nil {
		_ = in.Close()
		_ = out.Close()
		game.fail()
		return
	}
	go app.driveRoom(ctx, cancel, in, game)

	completed := false
	lines := bufio.NewScanner(out)
	lines.Buffer(make([]byte, 4096), transport.DefaultBinaryLimit+1)
	for lines.Scan() {
		data := append([]byte(nil), lines.Bytes()...)
		var message workerMessage
		if json.Unmarshal(data, &message) != nil || message.Match != game.id || message.To < -1 || message.To > 1 {
			cancel()
			break
		}
		if message.To < 0 {
			for _, client := range game.peers {
				client.send(data)
			}
		} else {
			game.peers[message.To].send(data)
		}
		completed = completed || (message.State != nil && message.State.Winner != -2)
	}
	if err := lines.Err(); err != nil {
		cancel()
		log.Printf("battle worker output: %v", err)
	}
	if err := cmd.Wait(); err != nil && ctx.Err() == nil {
		log.Printf("battle worker ended: %v", err)
	}
	if !completed && app.ctx.Err() == nil {
		game.fail()
	}
}

func (app *gateway) driveRoom(ctx context.Context, cancel func(), input io.WriteCloser, game *room) {
	defer input.Close()
	encoder := json.NewEncoder(input)
	if err := encoder.Encode(map[string]any{
		"type": "init", "match": game.id, "version": app.version,
		"first": game.peers[0].config, "second": game.peers[1].config,
	}); err != nil {
		cancel()
		return
	}

	a, b := game.peers[0].ctx.Done(), game.peers[1].ctx.Done()
	for {
		var action request
		select {
		case <-ctx.Done():
			return
		case <-a:
			action = request{Type: "disconnect", Match: game.id, Seat: 0}
			a = nil
		case <-b:
			action = request{Type: "disconnect", Match: game.id, Seat: 1}
			b = nil
		case action = <-game.commands:
		}
		if err := encoder.Encode(action); err != nil {
			return
		}
	}
}

func (game *room) fail() {
	for _, client := range game.peers {
		client.send(jsonBytes(notice{Type: "error", Code: "match_unavailable"}))
	}
}

func jsonBytes(value notice) []byte {
	data, _ := json.Marshal(value)
	return data
}

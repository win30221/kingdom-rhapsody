package main

import (
	"context"
	"encoding/json"
	"net/http/httptest"
	"os"
	"os/exec"
	"strings"
	"testing"
	"time"

	"github.com/coder/websocket"
	"github.com/win30221/kingdom-rhapsody/server/internal/gamenetkit/transport"
)

type testFrame struct {
	Type   string
	Match  string
	Code   string
	Seat   int
	Seq    int
	Result int
	State  struct {
		Tick, Winner, Cooldown int
		Energy                 float64
		Hand                   []string
		Actors                 []struct {
			ID, Card string
			S        int
			X, Y, HP float32
			Guard    bool
		}
	}
}

func TestTwoIndependentClients(t *testing.T) {
	dotnet := os.Getenv("KR_TEST_DOTNET")
	worker := os.Getenv("KR_TEST_WORKER")
	if dotnet == "" || worker == "" {
		t.Skip("set KR_TEST_DOTNET and KR_TEST_WORKER to run the real-worker integration test")
	}
	ctx, cancel := context.WithTimeout(context.Background(), 15*time.Second)
	defer cancel()
	data, err := exec.CommandContext(ctx, dotnet, worker, "--catalog").Output()
	if err != nil {
		t.Fatalf("catalog: %v", err)
	}
	var catalog workerCatalog
	if err := json.Unmarshal(data, &catalog); err != nil {
		t.Fatal(err)
	}

	app := newGateway(ctx, dotnet, worker, "../unity/Assets/Battle/Resources/Battle/Actors", catalog, 2)
	handler, err := app.routes("../unity/Packages/com.realtime.transport/Browser/transport.js", "", nil)
	if err != nil {
		t.Fatal(err)
	}
	http := httptest.NewServer(handler)
	defer http.Close()
	url := "ws" + strings.TrimPrefix(http.URL, "http") + "/ws"

	first := connect(t, ctx, url, catalog.Version, 0, 1,
		[]string{"bow-recruit", "shieldman", "cannoneer", "footman", "hammer-guard", "axe-guard", "frost-mage", "flame-mage"})
	defer first.Close()
	second := connect(t, ctx, url, catalog.Version, 1, 0,
		[]string{"meteorfall", "footman", "bow-recruit", "light-cavalry", "frost-mage", "hammer-guard", "crystal-armadillo", "shieldman"})
	defer second.Close()

	a := until(t, ctx, first, func(f testFrame) bool { return f.Type == "start" })
	b := until(t, ctx, second, func(f testFrame) bool { return f.Type == "start" })
	if a.Match == "" || a.Match != b.Match || a.Seat != 0 || b.Seat != 1 ||
		a.State.Hand[0] != "bow-recruit" || b.State.Hand[0] != "meteorfall" ||
		len(a.State.Actors) != 6 || len(b.State.Actors) != 6 {
		t.Fatalf("incorrect pairing, seats or private hands: %+v %+v", a, b)
	}
	guardFound := false
	for _, actor := range b.State.Actors {
		if actor.S == 1 && actor.Guard && actor.X > 0 && actor.Card == "royal-crossbowman" {
			guardFound = true
		}
	}
	if !guardFound {
		t.Fatal("seat B's local left guard was not placed on the world right")
	}

	inBattle := func(f testFrame) bool { return f.Type == "state" && f.State.Tick >= 60 }
	until(t, ctx, first, inBattle)
	until(t, ctx, second, inBattle)

	// A client-supplied opposing seat must never change the real command owner.
	send(t, ctx, second, request{Type: "play", Match: a.Match, Seq: 1, Seat: 0, Slot: 1, Card: "footman", X: .5, Y: -5.5})
	if got := ack(t, ctx, second, 1); got.Result != 5 || got.Seat != 1 {
		t.Fatalf("wrong territory response: %+v", got)
	}
	send(t, ctx, first, request{Type: "play", Match: a.Match, Seq: 1, Seat: 1, Slot: 0, Card: "bow-recruit", X: .5, Y: -5.5})
	send(t, ctx, second, request{Type: "play", Match: a.Match, Seq: 2, Slot: 0, Card: "meteorfall", X: -7.5, Y: -11.5})
	if got := ack(t, ctx, first, 1); got.Result != 0 || got.Seat != 0 {
		t.Fatalf("player A deploy: %+v", got)
	}
	if got := ack(t, ctx, second, 2); got.Result != 0 {
		t.Fatalf("player B spell: %+v", got)
	}

	send(t, ctx, first, request{Type: "sprint", Match: a.Match, Seq: 2})
	send(t, ctx, second, request{Type: "sprint", Match: a.Match, Seq: 3})
	if got := ack(t, ctx, first, 2); got.Result != 0 {
		t.Fatalf("player A sprint: %+v", got)
	}
	if got := ack(t, ctx, second, 3); got.Result != 3 {
		t.Fatalf("player B sprint should reject insufficient energy: %+v", got)
	}

	hit := func(f testFrame) bool {
		if f.Type != "state" {
			return false
		}
		bow, injured := false, false
		for _, actor := range f.State.Actors {
			bow = bow || (actor.S == 0 && actor.Card == "bow-recruit")
			injured = injured || (actor.S == 0 && actor.Guard && actor.X < 0 && actor.HP < 1)
		}
		return bow && injured
	}
	a = until(t, ctx, first, hit)
	b = until(t, ctx, second, hit)
	if a.State.Tick != b.State.Tick || a.State.Cooldown == 0 || b.State.Cooldown != 0 ||
		a.State.Hand[0] == "bow-recruit" || b.State.Hand[0] == "meteorfall" {
		t.Fatalf("shared damage/cooldown or rotation mismatch: %+v %+v", a, b)
	}

	second.Close()
	won := until(t, ctx, first, func(f testFrame) bool { return f.Type == "state" && f.State.Winner == 0 })
	if won.Match != a.Match {
		t.Fatal("disconnect resolved a different match")
	}
}

func connect(t *testing.T, ctx context.Context, url string, version, left, right int, deck []string) transport.BinaryWire {
	t.Helper()
	conn, _, err := websocket.Dial(ctx, url, nil)
	if err != nil {
		t.Fatal(err)
	}
	wire := transport.NewBinaryWebSocket(conn, transport.DefaultBinaryLimit)
	send(t, ctx, wire, request{Type: "queue", Version: version, Loadout: &loadout{&left, &right, deck}})
	until(t, ctx, wire, func(f testFrame) bool { return f.Type == "waiting" })
	return wire
}

func ack(t *testing.T, ctx context.Context, wire transport.BinaryWire, seq int) testFrame {
	t.Helper()
	return until(t, ctx, wire, func(f testFrame) bool { return f.Type == "ack" && f.Seq == seq })
}

func send(t *testing.T, ctx context.Context, wire transport.BinaryWire, action request) {
	t.Helper()
	data, err := json.Marshal(action)
	if err != nil {
		t.Fatal(err)
	}
	if err := wire.Write(ctx, data); err != nil {
		t.Fatal(err)
	}
}

func until(t *testing.T, ctx context.Context, wire transport.BinaryWire, condition func(testFrame) bool) testFrame {
	t.Helper()
	for {
		data, err := wire.Read(ctx)
		if err != nil {
			t.Fatal(err)
		}
		var frame testFrame
		if err := json.Unmarshal(data, &frame); err != nil {
			t.Fatal(err)
		}
		if frame.Type == "error" {
			t.Fatalf("server error: %s", frame.Code)
		}
		if condition(frame) {
			return frame
		}
	}
}

package main

import (
	"context"
	"encoding/json"
	"errors"
	"flag"
	"fmt"
	"log"
	"net/http"
	"os"
	"os/exec"
	"os/signal"
	"strings"
	"syscall"
	"time"
)

func main() {
	address := flag.String("http", "127.0.0.1:18120", "listen address")
	dotnet := flag.String("dotnet", "dotnet", ".NET executable")
	worker := flag.String("worker", "battle-worker/bin/Release/net8.0/BattleWorker.dll", "battle worker")
	actors := flag.String("actors", "../unity/Assets/Battle/Resources/Battle/Actors", "battle animation timing metadata")
	browser := flag.String("browser", "../unity/Assets/StreamingAssets/KingdomRhapsody/transport.js", "GameNetKit browser transport for the legacy /transport.js route")
	web := flag.String("web", "", "optional directory containing a Unity Web build")
	origins := flag.String("origins", "", "comma-separated HTTPS origin URLs for a separately hosted web build")
	capacity := flag.Int("rooms", 12, "maximum concurrent matches")
	flag.Parse()

	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()
	if *capacity < 1 || *capacity > 64 {
		log.Fatal("-rooms must be between 1 and 64")
	}
	cmd := exec.CommandContext(ctx, *dotnet, *worker, "--catalog")
	data, err := cmd.Output()
	if err != nil {
		log.Fatalf("battle worker catalog: %v", err)
	}
	var catalog workerCatalog
	if err := json.Unmarshal(data, &catalog); err != nil || catalog.Version <= 0 || len(catalog.Cards) < 8 {
		log.Fatal("invalid battle worker catalog")
	}
	if _, err := os.Stat(*actors); err != nil {
		log.Fatalf("actor timing directory: %v", err)
	}

	app := newGateway(ctx, *dotnet, *worker, *actors, catalog, *capacity)
	handler, err := app.routes(*browser, *web, strings.Split(*origins, ","))
	if err != nil {
		log.Fatal(err)
	}
	srv := &http.Server{Addr: *address, Handler: handler, ReadHeaderTimeout: 5 * time.Second}
	go func() {
		<-ctx.Done()
		shutdown, cancel := context.WithTimeout(context.Background(), 3*time.Second)
		defer cancel()
		_ = srv.Shutdown(shutdown)
	}()
	log.Printf("Kingdom Rhapsody · http://%s · %d rooms", *address, *capacity)
	if err := srv.ListenAndServe(); !errors.Is(err, http.ErrServerClosed) {
		log.Fatal(fmt.Errorf("serve battle gateway: %w", err))
	}
}

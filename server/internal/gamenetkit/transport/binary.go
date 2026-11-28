package transport

import (
	"context"
	"errors"
	"fmt"
	"io"

	"github.com/coder/websocket"
)

const DefaultBinaryLimit = 32 * 1024

var ErrBinaryRequired = errors.New("binary_required")
var ErrMessageTooLarge = errors.New("message_too_large")

// BinaryWire preserves message boundaries. Read returns owned bytes. Write must
// finish using its input before returning; callers must not mutate it during Write.
// A connection has one read owner. Close interrupts pending operations.
type BinaryWire interface {
	Read(context.Context) ([]byte, error)
	Write(context.Context, []byte) error
	Close()
}

// BinaryWebSocket is a fixed binary mode adapter, separate from the text Wire.
type BinaryWebSocket struct {
	conn  *websocket.Conn
	limit int
}

func NewBinaryWebSocket(conn *websocket.Conn, maxBytes int) *BinaryWebSocket {
	if maxBytes <= 0 {
		maxBytes = DefaultBinaryLimit
	}
	// Reader below bounds reassembly itself, giving a stable size error.
	conn.SetReadLimit(int64(maxBytes) + 1)
	return &BinaryWebSocket{conn: conn, limit: maxBytes}
}

func (w *BinaryWebSocket) Read(ctx context.Context) ([]byte, error) {
	kind, reader, err := w.conn.Reader(ctx)
	if err != nil {
		return nil, err
	}
	if kind != websocket.MessageBinary {
		w.Close()
		return nil, ErrBinaryRequired
	}
	data, err := io.ReadAll(io.LimitReader(reader, int64(w.limit)+1))
	if len(data) > w.limit {
		w.Close()
		return nil, fmt.Errorf("%w: limit %d", ErrMessageTooLarge, w.limit)
	}
	if err != nil {
		return nil, err
	}
	return data, nil
}

func (w *BinaryWebSocket) Write(ctx context.Context, data []byte) error {
	if len(data) > w.limit {
		return ErrMessageTooLarge
	}
	return w.conn.Write(ctx, websocket.MessageBinary, data)
}

func (w *BinaryWebSocket) Close() { _ = w.conn.CloseNow() }

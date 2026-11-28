# Kingdom Rhapsody 1v1 伺服器

需要 Go 1.23 以上及 .NET 8 SDK。從倉庫根目錄執行下列命令，Linux、macOS 和 Windows PowerShell 使用同一組相對路徑。

```sh
dotnet build -c Release server/battle-worker/BattleWorker.csproj
cd server
go run .
```

在同一台電腦開啟兩個 Unity 客戶端，從大廳進入「出戰」→「雙人對戰」，兩邊都填 `http://127.0.0.1:18120`。其中一端可用 Unity Editor，另一端使用相同提交的獨立 Player。需要跨區域網路時，由主機執行 `go run . -http 0.0.0.0:18120`，兩邊填主機的區域網路網址。用 `/healthz` 檢查 Go 行程。

Web 版可以用 `-web /path/to/web-build` 同源提供 Unity 成品及 GameNetKit 的 `/transport.js`。若將 Web 成品與伺服器放在不同網域，以 `-origins https://web.example.com` 設允許的來源，並用 HTTPS 反向代理轉發 `/ws` 以及 `/transport.js`。本機預設只監聽迴圈位址。

Go 服務使用 GameNetKit 的 Binary WebSocket 邊界處理，依加入順序配置雙方席次，轉交命令給每場專用的 .NET 戰鬥程序。`battle-worker` 從 Unity 專案編譯同一套 `BattleMatch` 程式碼，讀取相同的動畫命中設定。客戶端只送部署和疾行請求。對戰的身份、能量、部署、傷害和勝負由伺服器判定。掉線按棄權處理。結束可重新配對。可用 `-rooms` 調整最多同時進行的對戰數，預設 12。

## 驗證兩個連線

先完成上述 Release build，再從 `server/` 執行。

Linux、macOS

```sh
KR_TEST_DOTNET="$(command -v dotnet)" KR_TEST_WORKER="$PWD/battle-worker/bin/Release/net8.0/BattleWorker.dll" go test -race -v ./...
```

Windows PowerShell

```powershell
$env:KR_TEST_DOTNET = (Get-Command dotnet).Source
$env:KR_TEST_WORKER = (Resolve-Path .\battle-worker\bin\Release\net8.0\BattleWorker.dll).Path
go test -v ./...
```

整合測試使用兩個獨立的 WebSocket 連線和實際戰鬥程序，驗證配對、私有手牌、席次與左右護衛、部署和疾行扣費、相同 tick 的傷害同步，以及斷線結算。

GameNetKit 來源固定在 [8e923249f68d71a343a3224200494603ed8712ea](https://github.com/win30221/gamenetkit/tree/8e923249f68d71a343a3224200494603ed8712ea)。Unity 傳輸原檔在 `unity/Packages/com.realtime.transport/`，Go 傳輸原檔在 `server/internal/gamenetkit/transport/binary.go`。

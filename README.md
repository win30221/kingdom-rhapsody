# Kingdom Rhapsody

## 遊戲設計文件

- [20 張卡牌與戰鬥定位設計](docs/card-design.md)：兵種定位、能力及克制關係。
- [初始戰鬥數值 V0.1](docs/card-balance-v0.1.md)：血量、攻擊、移速、射程、消耗、技能與索敵類型。
- [戰場地圖與部署設計 V0.4](docs/battle-map-design.md)：三路地圖、格子部署、重疊部署區、中央危險區、王子環形巡邏與護衛 AI。

上述文件為設計與原型測試基準，尚非完整實作或正式平衡結果。

## Unity 大廳

取得完整分支後，用 Unity Hub 開啟 [`unity/`](unity) 專案，版本為 **Unity 6.6（`6000.6.8f1`）**。在 Project 視窗開啟 `Assets/Scenes/Lobby.unity` 並按 Play。Game 視窗可選 9:16，或用 1000×1568 對照設計比例。

- 左右護衛各顯示一張目前的圖卡。點開後可在長槍兵和弩手之間切換，上方角色同步更新。
- 帶兵牌組固定 8 個出戰格，上陣區與收藏區的卡牌採四欄對齊。收藏每頁顯示完整卡牌，可按箭頭、滑動卡牌區或用滑鼠滾輪切頁。點選卡牌後會浮出「看資訊」，未上陣的收藏卡另外顯示「上陣」。點「上陣」後，出戰卡牌亮框並微幅晃動，點任何出戰格即交換及儲存。點「取消」或 Esc 結束交換。
- 收藏分頁展示可用於 Unity Editor 與 Development Build，預設額外顯示 21 張帶 `DEMO` 標記的測試卡。測試卡沿用現有卡牌圖像，不能上陣。可在 `PortraitLobby` Inspector 的 `Collection Demo Count` 設為 0 關閉，或調整為 0–100，重新進入 Play 即生效。
- 卡牌資訊顯示上陣狀態與代表動作。兵種卡也顯示全身立繪。
- 已隨附涵蓋大廳與對練文案的繁中字型。新增文案後可使用 `tools/Prepare-LobbyFont.py` 更新字型子集。
- 按鈕壓下時縮放並變色，放開後回彈。點按、返回、確認和出戰各有 WAV 音效。右上角齒輪可以靜音。
- 護衛、牌組和音效設定會儲存在本機。滑鼠、觸控與方向鍵可操作，Enter 確認，Esc 關閉彈窗或返回大廳。

`PortraitLobby` 在 Play 時組合 uGUI Canvas。版面在 `Assets/Lobby/Scripts/PortraitLobby.cs`，圖層和 WAV 在 `Assets/Lobby/Resources/Lobby/`。卡牌縮圖會從同一個 repository 的 `art/` 匯入。更新精靈表後，可在 Unity 裡對對應的 `.lobbyportrait` 資產執行 Reimport。

點擊「出戰」會帶入已選的兩位護衛和八張卡，可選擇本機 AI 對練或正式雙人 1v1。[玩法與驗證](docs/battle-practice.md) · [雙人伺服器啟動方式](server/README.md)

[UI 設計預覽與原始素材](art/ui)

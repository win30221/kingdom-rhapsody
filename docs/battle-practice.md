# 本機對練

以 Unity 6000.6.8f1 開啟倉庫的 `unity` 目錄，播放 `Assets/Scenes/Lobby.unity`。點「出戰」帶入目前的八張牌和左右護衛。用滑鼠或觸控拖牌上場，也可先點牌再點一格。青色是己方部署權，琥珀色是雙方可部署區。選取「裂地波動」後，在戰場按住並拖曳可指定線段起點和朝向。另有王子疾行、暫停、再戰和返回大廳。

## 戰鬥規則

- 三路戰場的格子、危險區、王子巡邏、護衛駐點與解鎖座標沿用[戰場規格](battle-map-design.md)。
- 配置採用 [V0.1 數值](card-balance-v0.1.md)。近戰與遠程的普攻射程量測碰撞體邊緣。遊戲在攻擊圖的命中幀重查射程，法術以目標中心判斷是否命中。
- 沿用雙塔對決的四張手牌、四張待抽牌、起始 5 點能量、上限 10 點、每 3 秒回 1 點。對練依牌組順序發牌，有 2 秒倒數和 3 分鐘時限。王子先倒下的一方落敗。時間到比王子剩餘血量，相同則平手。
- 原型暈眩結束後有 0.6 秒抗暈時間。治療及穿山獸的百分比加傷限一般兵種與護衛。中路傷害、三張法術的王子傷害上限與疾行冷卻採文件的初始值。

## 共用元件與美術

- [GameNetKit](https://github.com/win30221/gamenetkit/tree/8e923249f68d71a343a3224200494603ed8712ea) 的 Core、Interpolation、Replication 封裝在 `unity/Packages/`。對練每秒推進 30 次並發布 15 次快照，畫面透過 `SnapshotBuffer`、`FixedTimeline` 與 `ReplicaStore` 取樣、插值及管理場上角色。
- 拖曳直接使用 Tower Duel 的 `DuelCardDrag.cs`，顯示面板也沿用同版本的 Panel Settings。大廳專案現有的卡框、立繪、陰影及操作音效供戰鬥畫面共用。
- 八向原圖由 `art/character-sprites/animation-roster.json` 列出，Unity 匯入器依角色讀取。更新原圖或幀資訊後，執行 `python3 tools/sync_battle_actors.py` 重新產生匯入描述，再開啟 Unity。

## 驗證

`dotnet run --project tools/battle-smoke/BattleSmoke.csproj` 在 .NET 8 驗證規則與 GameNetKit 快照播放。Unity 內可以從大廳進場，切換護衛配置、點牌和拖牌、暫停、結算後重開，並檢查直式畫面的安全區及效能。

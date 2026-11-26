# 《王國狂想》角色精靈表

| 角色與動作 | 透明原圖 | DXT5 | ASTC 4×4 | 單格 | 播放 |
| --- | --- | --- | --- | --- | --- |
| 王子走路 v3 | [PNG](prince/Prince_Walk.png) | [DDS](prince/Prince_Walk.dds) | [ASTC](prince/Prince_Walk.astc) | 192 × 192 | 12 FPS 循環 |
| 王子衝刺 v3 | [PNG](prince/Prince_Sprint.png) | [DDS](prince/Prince_Sprint.dds) | [ASTC](prince/Prince_Sprint.astc) | 192 × 192 | 20 FPS 循環 |
| 長槍走路 | [PNG](spearman/Spearman_Walk.png) | [DDS](spearman/Spearman_Walk.dds) | [ASTC](spearman/Spearman_Walk.astc) | 256 × 256 | 12 FPS 循環 |
| 長槍突刺 | [PNG](spearman/Spearman_Attack.png) | [DDS](spearman/Spearman_Attack.dds) | [ASTC](spearman/Spearman_Attack.astc) | 256 × 256 | 16 FPS 單次 |
| 弓手走路 | [PNG](archer/Archer_Walk.png) | [DDS](archer/Archer_Walk.dds) | [ASTC](archer/Archer_Walk.astc) | 256 × 256 | 12 FPS 循環 |
| 弓手射擊 | [PNG](archer/Archer_Attack.png) | [DDS](archer/Archer_Attack.dds) | [ASTC](archer/Archer_Attack.astc) | 256 × 256 | 16 FPS 單次 |

每張表有 12 欄、8 列，列序由上而下為 **N、NE、E、SE、S、SW、W、NW**。N 是背面，S 是正面。每列由左往右播放。王子精靈表為 2304 × 1536 px，長槍與弓手為 3072 × 2048 px。Unity 切格採用 Offset 0、Padding 0、Pivot `(0.5, 0.2)` 和 100 PPU。

移動動畫原地循環，由遊戲邏輯推進角色。長槍突刺與弓手放箭的演出點都是第 7 幀，從 0 起算為索引 6。

DXT5 使用有 Alpha 加權的 cluster-fit 編碼並存為 DDS。ASTC 使用 4×4 sRGB 編碼並存為含 16 位元組標頭的 ASTC 檔。兩者的尺寸、格線和 Alpha 通道均沿用 PNG，資料只含基礎層。Unity 可從透明 PNG 切格，並依發佈平台在匯入時選擇壓縮格式。DDS 和 ASTC 收錄獨立編碼後的貼圖資料，WebGL 整包大小需以打包成品量測。 [Arm ASTC 檔案格式](https://github.com/ARM-software/astc-encoder/blob/main/Docs/FileFormat.md)

[角色底稿與提示詞紀錄](PROMPTS.md)

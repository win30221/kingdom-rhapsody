# 《王國狂想》新增兵種動畫

十一位兵種各有八向走路和一套代表動作。每個方向由左到右排列 12 幀，上到下的方向為 **N、NE、E、SE、S、SW、W、NW**。
走路採 12 FPS 原地循環。其他動作採 16 FPS 單次播放，第 7 幀是主要出手姿勢。
切格採 Offset 0、Padding 0、100 PPU 及左下原點 Pivot `(0.5, 0.2)`。輕騎兵使用較寬的 320 px 單格容納人馬和騎槍，其餘是 256 px。

| 兵種 | 走路 | 代表動作 | 單格 |
| --- | --- | --- | --- |
| 盾兵 | [走路 PNG](shieldman/Shieldman_Walk.png) | [盾擊 PNG](shieldman/Shieldman_Bash.png) | 256 × 256 |
| 新手弓手 | [走路 PNG](bow-recruit/BowRecruit_Walk.png) | [射箭 PNG](bow-recruit/BowRecruit_Shoot.png) | 256 × 256 |
| 火炮槍兵 | [走路 PNG](cannoneer/Cannoneer_Walk.png) | [舉炮與後坐 PNG](cannoneer/Cannoneer_Fire.png) | 256 × 256 |
| 近戰小兵 | [走路 PNG](footman/Footman_Walk.png) | [短劍揮砍 PNG](footman/Footman_Slash.png) | 256 × 256 |
| 皇家軍旗手 | [走路 PNG](standard-bearer/StandardBearer_Walk.png) | [舉旗 PNG](standard-bearer/StandardBearer_Rally.png) | 256 × 256 |
| 戰地醫官 | [走路 PNG](field-medic/FieldMedic_Walk.png) | [遞出藥瓶 PNG](field-medic/FieldMedic_Heal.png) | 256 × 256 |
| 皇家輕騎兵 | [走路 PNG](light-cavalry/LightCavalry_Walk.png) | [衝鋒 PNG](light-cavalry/LightCavalry_Charge.png) | 320 × 320 |
| 重鎚兵 | [走路 PNG](hammer-guard/HammerGuard_Walk.png) | [重鎚砸地 PNG](hammer-guard/HammerGuard_Slam.png) | 256 × 256 |
| 巨斧兵 | [走路 PNG](axe-guard/AxeGuard_Walk.png) | [橫掃 PNG](axe-guard/AxeGuard_Sweep.png) | 256 × 256 |
| 冰霜術士 | [走路 PNG](frost-mage/FrostMage_Walk.png) | [冰霜施法 PNG](frost-mage/FrostMage_Cast.png) | 256 × 256 |
| 火焰術士 | [走路 PNG](flame-mage/FlameMage_Walk.png) | [火焰施法 PNG](flame-mage/FlameMage_Cast.png) | 256 × 256 |

[動畫資料](expanded-roster.json)記錄切格、播放速度與主要姿勢的影格索引，索引從零起算。

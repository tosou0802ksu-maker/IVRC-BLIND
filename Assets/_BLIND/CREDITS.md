# 外部アセットのクレジット

このプロジェクトで使っている配布アセットの一覧。
**「表記が要るもの」だけは提出物(動画エンドロール・展示パネル・README)に必ず載せること。**

---

## 表記が必須のもの（CC BY 等）

| アセット | 作者 | ライセンス | 使用箇所 |
|---|---|---|---|
| Skeletal Hand | Jeremy Swan | CC BY 4.0 | `Art/Models/PolyPizza/SkeletalHand_JeremySwan_CCBY.fbx` |
| Daruma | Ramujiro (@Ramzus) | CC BY 4.0 | `Art/Models/Daruma/Daruma_Ramujiro_CCBY/` — room12 のだるま（主役） |
| Daruma doll | Jan | CC BY 4.0 | `Art/Models/Daruma/Daruma_Jan_CCBY/` — room12 のだるま（色違い） |
| Daruma | Adrian Crisandy | CC BY 4.0 | `Art/Models/Daruma/Daruma_AdrianCrisandy_CCBY/` — room12 の遠景用だるま |
| neko-daruma | higan69 | CC BY 4.0 | `Art/Models/Daruma/Daruma_Neko_higan69_CCBY/` — room12 のだるま（変わり種） |

CC BY は **作者名の明記が義務**。以下の形で記載する。

> "Skeletal Hand" by Jeremy Swan — licensed under CC BY 4.0 (via Poly Pizza)
> "Daruma" by Ramujiro — licensed under CC BY 4.0 (via Sketchfab)
> "Daruma doll" by Jan — licensed under CC BY 4.0 (via Sketchfab)
> "Daruma" by Adrian Crisandy — licensed under CC BY 4.0 (via Sketchfab)
> "neko-daruma" by higan69 — licensed under CC BY 4.0 (via Sketchfab)

> だるま4種はいずれも glTF で入れているため、マテリアルが glTFast のシェーダ
> （うち2種は `glTF/Unlit` で光を受けない）になっていた。Standard に張り替え済み。
> 素の大きさもばらばら（1.0m / 0.05m / 1.97m / 1.79m）だったので、
> 配置時に高さ 0.22m へ正規化している。

---

## 表記が任意のもの（CC0 / パブリックドメイン）

CC0 は法的には**クレジット不要**。ただし作者への礼儀として載せておくのが慣例で、
IVRC の提出物でも「使用アセット一覧」として書いておくと審査時に説明が楽になる。

### room15（燃えるマネキンの部屋）で追加したもの

| アセット | 作者 / 配布元 | ライセンス | 使用箇所 |
|---|---|---|---|
| Animated Human | Quaternius (via OpenGameArt) | CC0 1.0 | `Art/Models/Quaternius/AnimatedHuman_Quaternius_CC0.fbx` — 歩くマネキン本体と Walk アニメーション |
| Particle Pack | Kenney | CC0 1.0 | `Art/Textures/Kenney_Particles/` — 炎・煙のスプライト、および壁床天井の煤汚れデカール |
| PaintedPlaster017 | ambientCG | CC0 1.0 | `Art/Textures/PaintedPlaster017/` — 壁 |
| Carpet016 | ambientCG | CC0 1.0 | `Art/Textures/Carpet016/` — 床のカーペット |
| Wood067 | ambientCG | CC0 1.0 | `Art/Textures/Wood067/` — 中央の扉 |

### room16（人形の間）で追加したもの

| アセット | 作者 / 配布元 | ライセンス | 使用箇所 |
|---|---|---|---|
| Herringbone Parquet | Sergej Majboroda（撮影）/ Jenelle van Heerden（加工）— Poly Haven | CC0 1.0 | `Art/Textures/herringbone_parquet/` — 床の縁の寄木 |
| Wood067 | ambientCG | CC0 1.0 | `Art/Textures/Wood067/` — 壁の腰板、格天井の梁（room15の扉と共用） |
| PaintedPlaster017 | ambientCG | CC0 1.0 | `Art/Textures/PaintedPlaster017/` — 格天井の鏡板（room15の壁と共用） |

| Wooden Bookshelf Worn | Poly Haven | CC0 1.0 | `wood_bookshelf/` — 棚 |

> `quatrefoil_jacquard_fabric`（Poly Haven, CC0）も取得済みだが、
> 絨毯は自作テクスチャに差し替えたため現在は未使用。

**自作（外部素材ではないのでクレジット不要）**

| ファイル | 内容 |
|---|---|
| `Art/Textures/Generated/Room16_RugPattern.png` | ペルシャ絨毯風の模様。CC0でこの手の緞通が見つからなかったのでUnity内でコードから生成した |
| `Art/Models/Room16Dolls/*.asset` | Free Doll のスキンメッシュにポーズを付けて焼き直した静的メッシュ。元データのライセンスは下記 Free Doll Character に従う |

---

## Unity Asset Store（Standard Unity Asset Store EULA）

CC ライセンスではないので**扱いが別**。EULA 上、完成物（作品）への組み込みと配布は可、
アセット素材そのものの再配布は不可。クレジット表記は義務ではないが載せておく。

| アセット | 作者 | 使用箇所 |
|---|---|---|
| Free Doll Character | RamsterZ | `RamsterZ_FreeDoll/` — room16 のマネキン、天井から降りてくる巨大な腕、家具の上に載せた人形のパーツ。マテリアルは化粧を消した自作の石膏マテリアルに差し替えている |
| Modular Sewer Props | Ata Khani | `Ata Khani/Modular Sewer Props/` — 木箱、樽、布のかかった木箱のかたまり、床の板、脚立 |
| Mix Furniture Pack | ZNS3D | `ZNS3D/Mix_Furniture_Pack/` — 壁ぎわの本棚、引き出し、ティーテーブル |

> **Mix Furniture Pack はマテリアルが HDRP 製で、そのままだとビルトインRPでマゼンタになる。**
> このプロジェクトでは Standard シェーダに張り替えて `_MainTex` / `_BumpMap` を繋ぎ直してある
> （`Assets/ZNS3D/Mix_Furniture_Pack/Materials/`）。再インポートすると元に戻るので注意。

> **注意**：これらはソース素材そのものなので、
> Git 公開リポジトリに置いたままにすると EULA 上グレー。提出前に扱いを決めること。

---

## 出所が未確認のもの（提出前に要確認）

| アセット | 状況 | 使用箇所 |
|---|---|---|
| MannequinAsset | 付属の `READ ME.txt` に配布元・ライセンス記載なし | `Prop_ShelfDecor` — 棚の上に置いた人形のパーツ |

入手元が思い出せない場合は、Free Doll のパーツ FBX
（`KillerDollPartsArmR01` 等）に差し替えれば room16 から MannequinAsset 依存を消せる。

### それ以前から使っているもの

| アセット | 作者 / 配布元 | ライセンス | 使用箇所 |
|---|---|---|---|
| Rubber Duck Toy | Poly Haven | CC0 1.0 | `Art/Models/PolyHaven/RubberDuck/` — room7 のアヒル |
| Wet Floor Sign 01 | Fran Calvente (Poly Haven) | CC0 1.0 | `Art/Models/PolyHaven/WetFloorSign/` |
| Rail Corner | Quaternius (via Poly Pizza) | CC0 1.0 | `Art/Models/PolyPizza/RailCorner_Quaternius_CC0.fbx` |
| Spot Light | iPoly3D (via Poly Pizza) | CC0 1.0 | `Art/Models/PolyPizza/SpotLight_iPoly3D_CC0.fbx` |
| 各種 Tiles / Concrete / OfficeCeiling テクスチャ | ambientCG | CC0 1.0 | `Art/Textures/` |

---

## 配布元URL

- Quaternius: https://quaternius.com/ / https://opengameart.org/content/animated-human-low-poly
- Kenney: https://kenney.nl/assets/particle-pack
- ambientCG: https://ambientcg.com/
- Poly Haven: https://polyhaven.com/
- Poly Pizza: https://poly.pizza/

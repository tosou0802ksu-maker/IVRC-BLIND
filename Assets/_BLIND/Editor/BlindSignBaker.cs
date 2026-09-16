using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BLIND.EditorTools
{
    /// <summary>
    /// 看板の文字をテクスチャに焼く。
    ///
    /// なぜ TextMeshPro を使わないか:
    ///   ・日本語を出すには CJK のフォント資産が要る。素の LiberationSans SDF だと
    ///     全部 豆腐(□) になる。フォント資産を足すとアトラスだけで数十MBになり、
    ///     しかも**ライセンス表記が増える**（この作品は表記不要の素材で通している）。
    ///   ・看板の文言は開発中に何度も変わるが、**実行時に変わる必要は無い**。
    ///     焼いてしまえば実行時のフォント依存がゼロになる。
    ///
    /// OS のフォントを使って編集時に一度だけ描き、PNG として保存する。
    /// 焼いた後は普通の画像なので、実機でもエディタでも同じ絵になる。
    /// </summary>
    public static class BlindSignBaker
    {
        const string FontName = "Hiragino Sans Bold";   // macOS 標準。日本語が入っている
        const int Px = 160;                              // 描画時の文字の大きさ(px)

        /// <summary>
        /// 文字を焼いて PNG 資産にする。既にあれば上書きする。
        /// </summary>
        /// <param name="lines">行ごとの文字列。行の高さは自動で割り振る。</param>
        /// <param name="assetPath">Assets/… で始まる .png のパス</param>
        /// <param name="w">テクスチャの幅(px)</param>
        /// <param name="h">テクスチャの高さ(px)</param>
        /// <param name="bg">下地の色</param>
        /// <param name="fg">文字の色</param>
        /// <param name="margin">左右の余白の割合(0〜0.4)</param>
        public static Texture2D Bake(string[] lines, string assetPath, int w, int h,
                                     Color bg, Color fg, float margin = 0.08f)
        {
            var font = Font.CreateDynamicFontFromOSFont(FontName, Px);
            if (font == null) { Debug.LogError("BLIND: フォントが無い " + FontName); return null; }

            // ⚠️ 文字を要求してからでないとアトラスに無い。
            //    要求の前に GetCharacterInfo を呼ぶと全部 0 が返って**何も描かれない**。
            var all = string.Concat(lines);
            font.RequestCharactersInTexture(all, Px, FontStyle.Bold);

            var rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(true, true, bg);

            var mat = new Material(Shader.Find("GUI/Text Shader"));
            mat.mainTexture = font.material.mainTexture;
            mat.color = fg;

            float rowH = (float)h / lines.Length;

            GL.PushMatrix();
            GL.LoadPixelMatrix(0, w, h, 0);          // 左上原点。CharacterInfo と向きを合わせる
            for (int li = 0; li < lines.Length; li++)
            {
                var line = lines[li];

                // 行の実寸を測って、はみ出すなら縮める
                float raw = 0f;
                foreach (var ch in line)
                {
                    if (font.GetCharacterInfo(ch, out var ci, Px, FontStyle.Bold)) raw += ci.advance;
                }
                if (raw <= 0.01f) continue;

                float usable = w * (1f - margin * 2f);
                float scale = Mathf.Min(usable / raw, rowH * 0.72f / Px);
                float x = (w - raw * scale) * 0.5f;
                float baseline = rowH * li + rowH * 0.5f + Px * scale * 0.36f;

                foreach (var ch in line)
                {
                    if (!font.GetCharacterInfo(ch, out var ci, Px, FontStyle.Bold)) continue;

                    var r = new Rect(x + ci.minX * scale,
                                     baseline - ci.maxY * scale,
                                     (ci.maxX - ci.minX) * scale,
                                     (ci.maxY - ci.minY) * scale);

                    // CharacterInfo の uv は4隅で来る。回転しているフォントもあるので4点を使う。
                    GL.Begin(GL.QUADS);
                    mat.SetPass(0);
                    GL.Color(fg);
                    GL.TexCoord(ci.uvTopLeft);     GL.Vertex3(r.xMin, r.yMin, 0);
                    GL.TexCoord(ci.uvTopRight);    GL.Vertex3(r.xMax, r.yMin, 0);
                    GL.TexCoord(ci.uvBottomRight); GL.Vertex3(r.xMax, r.yMax, 0);
                    GL.TexCoord(ci.uvBottomLeft);  GL.Vertex3(r.xMin, r.yMax, 0);
                    GL.End();

                    x += ci.advance * scale;
                }
            }
            GL.PopMatrix();

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            rt.Release();
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(mat);

            var dir = System.IO.Path.GetDirectoryName(assetPath);
            if (!AssetDatabase.IsValidFolder(dir)) CreateFolders(dir);
            System.IO.File.WriteAllBytes(assetPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

            var imp = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (imp != null)
            {
                imp.textureType = TextureImporterType.Default;
                imp.mipmapEnabled = true;
                imp.wrapMode = TextureWrapMode.Clamp;
                imp.filterMode = FilterMode.Trilinear;
                imp.anisoLevel = 8;                   // 斜めから読むので効く
                imp.maxTextureSize = Mathf.Max(w, h);
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }

        static void CreateFolders(string dir)
        {
            var parts = dir.Split('/');
            var cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                var next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }
    }
}

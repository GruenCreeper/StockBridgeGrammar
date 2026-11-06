using System.Drawing;
using System.Drawing.Drawing2D;

namespace StockBridgeGrammar.Utils
{
    // Programmatically drawn 24×24 component icons. Avoids the embedded-
    // resource ceremony — every component just returns IconFactory.X.
    // Each icon is generated once and cached.
    public static class IconFactory
    {
        private const int Size = 24;
        private static readonly Color Bg     = Color.FromArgb(255, 30, 41, 59);
        private static readonly Color Accent = Color.FromArgb(255, 56, 189, 248);
        private static readonly Color Ok     = Color.FromArgb(255, 52, 211, 153);
        private static readonly Color Warn   = Color.FromArgb(255, 251, 191, 36);
        private static readonly Color Pale   = Color.FromArgb(255, 226, 232, 240);

        private static Bitmap _recognize, _assign, _apply, _generate, _decode;
        private static Bitmap _exampleStock, _ruleLibrary, _stockItem,
                              _stockInventory, _ruleParams, _targetPicker,
                              _candidateSelector;

        // ----- per-component accessors (cached) -----
        public static Bitmap Recognize        => _recognize        ?? (_recognize        = MakeRecognize());
        public static Bitmap Assign           => _assign           ?? (_assign           = MakeAssign());
        public static Bitmap Apply            => _apply            ?? (_apply            = MakeApply());
        public static Bitmap Generate         => _generate         ?? (_generate         = MakeGenerate());
        public static Bitmap Decode           => _decode           ?? (_decode           = MakeDecode());
        public static Bitmap ExampleStock     => _exampleStock     ?? (_exampleStock     = MakeExampleStock());
        public static Bitmap RuleLibrary      => _ruleLibrary      ?? (_ruleLibrary      = MakeRuleLibrary());
        public static Bitmap StockItem        => _stockItem        ?? (_stockItem        = MakeStockItem());
        public static Bitmap StockInventoryBuilder => _stockInventory ?? (_stockInventory = MakeStockInventory());
        public static Bitmap RuleParams       => _ruleParams       ?? (_ruleParams       = MakeRuleParams());
        public static Bitmap TargetPicker     => _targetPicker     ?? (_targetPicker     = MakeTargetPicker());
        public static Bitmap CandidateSelector=> _candidateSelector?? (_candidateSelector= MakeCandidateSelector());

        // ----- helpers -----
        private static Bitmap NewBitmap(out Graphics g)
        {
            var bmp = new Bitmap(Size, Size);
            g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Bg);
            return bmp;
        }

        // 1. Recognize — node-line graph: two chord lines + 3 verticals + 2 nodes.
        private static Bitmap MakeRecognize()
        {
            var bmp = NewBitmap(out var g);
            using (var p = new Pen(Accent, 1.6f))
            {
                g.DrawLine(p, 3, 6, 21, 6);     // top chord
                g.DrawLine(p, 3, 18, 21, 18);   // bottom chord
                g.DrawLine(p, 7, 6, 12, 18);    // diagonal
                g.DrawLine(p, 12, 6, 17, 18);   // diagonal
                g.DrawLine(p, 7, 18, 12, 6);    // diagonal
            }
            using (var b = new SolidBrush(Pale))
            {
                foreach (var (x, y) in new[] { (3,6),(12,6),(21,6),(3,18),(12,18),(21,18) })
                    g.FillEllipse(b, x - 2, y - 2, 4, 4);
            }
            g.Dispose();
            return bmp;
        }

        // 2. Assign — board + checkmark.
        private static Bitmap MakeAssign()
        {
            var bmp = NewBitmap(out var g);
            using (var b = new SolidBrush(Color.FromArgb(255, 180, 83, 9)))
                g.FillRectangle(b, 3, 10, 14, 5);
            using (var p = new Pen(Ok, 2.4f))
            {
                g.DrawLine(p, 13, 16, 17, 20);
                g.DrawLine(p, 17, 20, 22, 11);
            }
            g.Dispose();
            return bmp;
        }

        // 3. Apply Rule — input → arrow → output.
        private static Bitmap MakeApply()
        {
            var bmp = NewBitmap(out var g);
            using (var p = new Pen(Pale, 1.8f))
            {
                g.DrawLine(p, 3, 12, 9, 12);
                g.DrawLine(p, 15, 12, 21, 12);
            }
            using (var b = new SolidBrush(Accent))
            {
                g.FillPolygon(b, new[] { new Point(9, 8), new Point(15, 12), new Point(9, 16) });
            }
            using (var b = new SolidBrush(Pale))
            {
                g.FillEllipse(b, 2, 10, 4, 4);
                g.FillEllipse(b, 18, 10, 4, 4);
            }
            g.Dispose();
            return bmp;
        }

        // 4. Generate Candidates — three branching alternatives.
        private static Bitmap MakeGenerate()
        {
            var bmp = NewBitmap(out var g);
            using (var p = new Pen(Accent, 1.6f))
            {
                g.DrawLine(p, 4, 12, 10, 12);
                g.DrawLine(p, 10, 12, 18, 5);
                g.DrawLine(p, 10, 12, 18, 12);
                g.DrawLine(p, 10, 12, 18, 19);
            }
            using (var b = new SolidBrush(Ok))
            {
                g.FillEllipse(b, 4, 10, 4, 4);
                g.FillEllipse(b, 8, 10, 4, 4);
            }
            using (var b = new SolidBrush(Warn))
            {
                g.FillEllipse(b, 17, 3, 4, 4);
                g.FillEllipse(b, 17, 10, 4, 4);
                g.FillEllipse(b, 17, 17, 4, 4);
            }
            g.Dispose();
            return bmp;
        }

        // 5. Decode Graph — JSON braces → lines/points.
        private static Bitmap MakeDecode()
        {
            var bmp = NewBitmap(out var g);
            using (var f = new Font("Consolas", 9, FontStyle.Bold))
            using (var b = new SolidBrush(Warn))
                g.DrawString("{ }", f, b, 1, 4);
            using (var p = new Pen(Accent, 1.5f))
                g.DrawLine(p, 12, 18, 22, 12);
            using (var b = new SolidBrush(Pale))
            {
                g.FillEllipse(b, 11, 17, 4, 4);
                g.FillEllipse(b, 21, 11, 4, 4);
            }
            g.Dispose();
            return bmp;
        }

        // 6. Example Stock — stacked boards.
        private static Bitmap MakeExampleStock()
        {
            var bmp = NewBitmap(out var g);
            using (var b = new SolidBrush(Color.FromArgb(255, 180, 83, 9)))
            {
                g.FillRectangle(b, 3, 5, 18, 3);
                g.FillRectangle(b, 3, 11, 18, 3);
                g.FillRectangle(b, 3, 17, 18, 3);
            }
            using (var p = new Pen(Pale, 0.8f))
            {
                g.DrawRectangle(p, 3, 5, 18, 3);
                g.DrawRectangle(p, 3, 11, 18, 3);
                g.DrawRectangle(p, 3, 17, 18, 3);
            }
            g.Dispose();
            return bmp;
        }

        // 7. Rule Library — book.
        private static Bitmap MakeRuleLibrary()
        {
            var bmp = NewBitmap(out var g);
            using (var b = new SolidBrush(Accent))
                g.FillRectangle(b, 3, 4, 18, 16);
            using (var b = new SolidBrush(Bg))
                g.FillRectangle(b, 5, 6, 14, 12);
            using (var p = new Pen(Pale, 1.0f))
            {
                g.DrawLine(p, 7, 9, 17, 9);
                g.DrawLine(p, 7, 12, 17, 12);
                g.DrawLine(p, 7, 15, 14, 15);
            }
            g.Dispose();
            return bmp;
        }

        // 8. Stock Item — one board.
        private static Bitmap MakeStockItem()
        {
            var bmp = NewBitmap(out var g);
            using (var b = new SolidBrush(Color.FromArgb(255, 180, 83, 9)))
                g.FillRectangle(b, 3, 9, 18, 6);
            using (var p = new Pen(Pale, 1.0f))
                g.DrawRectangle(p, 3, 9, 18, 6);
            g.Dispose();
            return bmp;
        }

        // 9. Stock Inventory Builder — stack + plus.
        private static Bitmap MakeStockInventory()
        {
            var bmp = MakeExampleStock();
            using (var g = Graphics.FromImage(bmp))
            using (var p = new Pen(Ok, 2.4f))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.DrawLine(p, 19, 19, 22, 22);
                g.DrawLine(p, 22, 19, 19, 22);
            }
            return bmp;
        }

        // 10. Rule Params — three slider tracks with knobs.
        private static Bitmap MakeRuleParams()
        {
            var bmp = NewBitmap(out var g);
            using (var p = new Pen(Pale, 1.4f))
            {
                g.DrawLine(p, 3, 7, 21, 7);
                g.DrawLine(p, 3, 12, 21, 12);
                g.DrawLine(p, 3, 17, 21, 17);
            }
            using (var b = new SolidBrush(Accent))
            {
                g.FillEllipse(b, 8, 4, 6, 6);
                g.FillEllipse(b, 14, 9, 6, 6);
                g.FillEllipse(b, 5, 14, 6, 6);
            }
            g.Dispose();
            return bmp;
        }

        // 11. Target Picker — crosshair.
        private static Bitmap MakeTargetPicker()
        {
            var bmp = NewBitmap(out var g);
            using (var p = new Pen(Warn, 1.6f))
            {
                g.DrawEllipse(p, 5, 5, 14, 14);
                g.DrawEllipse(p, 9, 9, 6, 6);
                g.DrawLine(p, 12, 1, 12, 6);
                g.DrawLine(p, 12, 18, 12, 23);
                g.DrawLine(p, 1, 12, 6, 12);
                g.DrawLine(p, 18, 12, 23, 12);
            }
            using (var b = new SolidBrush(Warn))
                g.FillEllipse(b, 11, 11, 2, 2);
            g.Dispose();
            return bmp;
        }

        // 12. Candidate Selector — numbered cards.
        private static Bitmap MakeCandidateSelector()
        {
            var bmp = NewBitmap(out var g);
            using (var b = new SolidBrush(Color.FromArgb(255, 56, 100, 180)))
                g.FillRectangle(b, 3, 6, 6, 14);
            using (var b = new SolidBrush(Ok))
                g.FillRectangle(b, 9, 4, 6, 16);
            using (var b = new SolidBrush(Color.FromArgb(255, 56, 100, 180)))
                g.FillRectangle(b, 15, 6, 6, 14);
            using (var p = new Pen(Pale, 1.0f))
            {
                g.DrawRectangle(p, 3, 6, 6, 14);
                g.DrawRectangle(p, 9, 4, 6, 16);
                g.DrawRectangle(p, 15, 6, 6, 14);
            }
            g.Dispose();
            return bmp;
        }
    }
}

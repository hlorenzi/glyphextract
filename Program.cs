using System;
using System.Drawing;
using System.Windows.Media;
using System.IO;


namespace GlyphExtract
{
    class Program
    {
        static void WriteText(Stream s, string str)
        {
            byte[] name = System.Text.Encoding.UTF8.GetBytes(str);
            s.Write(name, 0, name.Length);
        }


        static void Main(string[] args)
        {
            Console.WriteLine("GlyphExtract v0.1");
            Console.WriteLine("Copyright 2016 Henrique Lorenzi");
            Console.WriteLine("Build date: 20 apr 2016");

            var parser = new Util.ParameterParser();
            var paramOutput = parser.Add("out", "glyph#", "The name of the output files without extension; may contain a path. # will be substituted by the glyph index.");
            var paramSize = parser.Add("size", "32", "The size in which to render glyphs.");
            var paramCodepoints = parser.Add("codepoints", "0x00-0xff", "The list of Unicode codepoints to extract, in the format: $a,$b-$z,32,33,34-40,0x1af,0x200-0x3ff");
            var paramRenderMode = parser.Add("render-mode", "antialias-hint", "The glyph rendering mode, namely: binary, binary-hint, antialias, antialias-hint, cleartype");
            var paramDebug = parser.Add("debug", "off", "Whether to draw debug information.");
            var paramColor = parser.Add("color", "alpha-black", "The color to render the glyphs in, namely: alpha-white, alpha-black, grayscale");

            if (!parser.Parse(args) ||
                parser.GetUnnamed().Count != 1)
            {
                Console.WriteLine("Parameters:");
                parser.PrintHelp("  ");
                return;
            }

            var inputFile = parser.GetUnnamed()[0];
            var glyphTypeface = new GlyphTypeface(new Uri("file:///" + Path.GetFullPath(inputFile)));

            var size = paramSize.GetInt();
            var codepoints = paramCodepoints.GetIntList();
            var drawDebug = paramDebug.GetBool();

            var color = System.Drawing.Color.White;
            var bkgColor = System.Drawing.Color.FromArgb(0, color);
            switch (paramColor.GetString())
            {
                case "alpha-white":
                    color = System.Drawing.Color.White;
                    bkgColor = System.Drawing.Color.FromArgb(0, color);
                    break;

                case "alpha-black":
                    color = System.Drawing.Color.Black;
                    bkgColor = System.Drawing.Color.FromArgb(0, color);
                    break;

                /* FIXME: Bad interaction with glyph bounding box calculation */
                case "grayscale":
                    color = System.Drawing.Color.White;
                    bkgColor = System.Drawing.Color.Black;
                    break;
            }

            var renderHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;
            switch (paramRenderMode.GetString())
            {
                case "binary": renderHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixel; break;
                case "binary-hint": renderHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit; break;
                case "antialias": renderHint = System.Drawing.Text.TextRenderingHint.AntiAlias; break;
                case "antialias-hint": renderHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit; break;
                case "cleartype": renderHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit; break;
            }

            using (var fontColl = new System.Drawing.Text.PrivateFontCollection())
            {
                fontColl.AddFontFile(inputFile);
                var font = new Font(fontColl.Families[0], size);

                foreach (var codepoint in codepoints)
                {
                    Console.Write("Extracting glyph for codepoint 0x" + codepoint.ToString("x4") + "...");

                    ushort glyph;
                    if (!glyphTypeface.CharacterToGlyphMap.TryGetValue(codepoint, out glyph))
                    {
                        Console.WriteLine(" no glyph found.");
                        continue;
                    }

                    var bitmapWidth = (int)((glyphTypeface.AdvanceWidths[glyph] - glyphTypeface.LeftSideBearings[glyph]) * size * 1.3f);
                    var bitmapHeight = (int)((glyphTypeface.Baseline - glyphTypeface.TopSideBearings[glyph]) * size * 1.3f);

                    if (bitmapWidth <= 0 || bitmapHeight <= 0)
                    {
                        Console.WriteLine(" blank glyph.");
                        continue;
                    }

                    var bitmap = new Bitmap(bitmapWidth * 2, bitmapHeight * 2);
                    var graphics = Graphics.FromImage(bitmap);

                    graphics.TextRenderingHint = renderHint;

                    var format = new StringFormat();
                    format.Alignment = StringAlignment.Center;
                    format.LineAlignment = StringAlignment.Center;

                    graphics.FillRectangle(
                        new SolidBrush(bkgColor),
                        0, 0, bitmapWidth, bitmapHeight);

                    graphics.DrawString(
                        new string((char)codepoint, 1),
                        font,
                        new SolidBrush(color),
                        new PointF(bitmapWidth, bitmapHeight),
                        format);

                    var topmostDrawn = bitmap.Height;
                    var leftmostDrawn = bitmap.Width;
                    var bottommostDrawn = 0;
                    var rightmostDrawn = 0;

                    var bitmapData = bitmap.LockBits(
                        new Rectangle(0, 0, bitmapWidth, bitmapHeight),
                        System.Drawing.Imaging.ImageLockMode.ReadWrite,
                        System.Drawing.Imaging.PixelFormat.Format32bppArgb);

                    unsafe
                    {
                        var bitmapPtrBase = (byte*)bitmapData.Scan0.ToPointer();

                        for (var j = 0; j < bitmap.Height; j++)
                        {
                            for (var i = 0; i < bitmap.Width; i++)
                            {
                                var bitmapPtr =
                                    bitmapPtrBase +
                                    j * bitmapData.Stride +
                                    i * 4;

                                if (bitmapPtr[3] != 0)
                                {
                                    leftmostDrawn = Math.Min(i, leftmostDrawn);
                                    topmostDrawn = Math.Min(j, topmostDrawn);
                                    rightmostDrawn = Math.Max(i + 1, rightmostDrawn);
                                    bottommostDrawn = Math.Max(j + 1, bottommostDrawn);
                                }
                                else
                                {
                                    bitmapPtr[2] = color.R;
                                    bitmapPtr[1] = color.G;
                                    bitmapPtr[0] = color.B;
                                }
                            }
                        }
                    }

                    bitmap.UnlockBits(bitmapData);

                    if (topmostDrawn >= bottommostDrawn || leftmostDrawn >= rightmostDrawn)
                    {
                        Console.WriteLine(" blank glyph.");
                        continue;
                    }

                    var left = leftmostDrawn - (float)glyphTypeface.LeftSideBearings[glyph] * size * 1.3f;
                    var right = leftmostDrawn + (float)(glyphTypeface.AdvanceWidths[glyph] - glyphTypeface.LeftSideBearings[glyph]) * size * 1.3f;
                    var top = topmostDrawn - (float)(glyphTypeface.TopSideBearings[glyph]) * size * 1.3f;
                    var baseline = topmostDrawn + (float)(glyphTypeface.CapsHeight - glyphTypeface.TopSideBearings[glyph]) * size * 1.3f;
                    var bottom = topmostDrawn + (float)(glyphTypeface.Baseline - glyphTypeface.TopSideBearings[glyph]) * size * 1.3f;

                    if (drawDebug)
                    {
                        graphics.DrawRectangle(
                            new System.Drawing.Pen(System.Drawing.Color.Red),
                            leftmostDrawn,
                            topmostDrawn,
                            rightmostDrawn - leftmostDrawn,
                            bottommostDrawn - topmostDrawn);

                        graphics.DrawLine(new System.Drawing.Pen(System.Drawing.Color.Blue), left, -1000, left, 1000);
                        graphics.DrawLine(new System.Drawing.Pen(System.Drawing.Color.Blue), right, -1000, right, 1000);
                        graphics.DrawLine(new System.Drawing.Pen(System.Drawing.Color.Blue), -1000, top, 1000, top);
                        graphics.DrawLine(new System.Drawing.Pen(System.Drawing.Color.Red), -1000, baseline, 1000, baseline);
                        graphics.DrawLine(new System.Drawing.Pen(System.Drawing.Color.Blue), -1000, bottom, 1000, bottom);
                    }

                    var outName = Path.GetFullPath(paramOutput.GetString()).Replace("#", codepoint.ToString("x4"));
                    var outDir = Path.GetDirectoryName(outName);
                    if (!Directory.Exists(outDir))
                        Directory.CreateDirectory(outDir);

                    bitmap.Save(outName + ".png");

                    using (FileStream glyphXml = new FileStream(outName + ".sprsheet", FileMode.Create))
                    {
                        WriteText(glyphXml, "<sprite-sheet src=\"" + Path.GetFileName(outName + ".png") + "\">\n");
                        WriteText(glyphXml, "\t<sprite ");
                        WriteText(glyphXml, "name=\"" + codepoint.ToString("X4") + "\" ");
                        WriteText(glyphXml, "x=\"0\" ");
                        WriteText(glyphXml, "y=\"0\" ");
                        WriteText(glyphXml, "width=\"" + bitmap.Width + "\" ");
                        WriteText(glyphXml, "height=\"" + bitmap.Height + "\" ");
                        WriteText(glyphXml, "crop-left=\"" + leftmostDrawn + "\" ");
                        WriteText(glyphXml, "crop-right=\"" + (bitmap.Width - rightmostDrawn) + "\" ");
                        WriteText(glyphXml, "crop-top=\"" + topmostDrawn + "\" ");
                        WriteText(glyphXml, "crop-bottom=\"" + (bitmap.Height - bottommostDrawn) + "\">\n");

                        WriteText(glyphXml, "\t\t<guide name=\"unicode\" kind=\"int\" value=\"" + codepoint + "\"></guide>\n");
                        WriteText(glyphXml, "\t\t<guide name=\"y-offset\" kind=\"int\" value=\"" + Math.Floor(baseline) + "\"></guide>\n");
                        WriteText(glyphXml, "\t\t<guide name=\"x-offset\" kind=\"int\" value=\"" + Math.Floor(left) + "\"></guide>\n");
                        WriteText(glyphXml, "\t\t<guide name=\"x-advance\" kind=\"int\" value=\"" + Math.Floor(right - left) + "\"></guide>\n");

                        WriteText(glyphXml, "\t</sprite>\n");
                        WriteText(glyphXml, "</sprite-sheet>\n");
                    }

                    Console.WriteLine(" done.");
                }
            }
        }
    }
}

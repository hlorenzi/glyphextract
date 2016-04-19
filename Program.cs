using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Drawing;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;

namespace GlyphExtract
{
    class Program
    {
        class GlyphData
        {
            public int glyphIndex;
            public int fileIndex;
            public int baseline;
            public int xOffset;
            public int advanceWidth;
            public int width, height;
        }

        static void WriteText(Stream s, string str)
        {
            byte[] name = System.Text.Encoding.UTF8.GetBytes(str);
            s.Write(name, 0, name.Length);
        }

        public static String MakeRelativePath(String fromPath, String toPath)
        {
            if (String.IsNullOrEmpty(fromPath)) throw new ArgumentNullException("fromPath");
            if (String.IsNullOrEmpty(toPath)) throw new ArgumentNullException("toPath");

            Uri fromUri = new Uri(fromPath);
            Uri toUri = new Uri(toPath);

            if (fromUri.Scheme != toUri.Scheme) { return toPath; } // path can't be made relative.

            Uri relativeUri = fromUri.MakeRelativeUri(toUri);
            String relativePath = Uri.UnescapeDataString(relativeUri.ToString());

            if (toUri.Scheme.ToUpperInvariant() == "FILE")
            {
                relativePath = relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            }

            return relativePath;
        }

        
        static Dictionary<string, string> ParseArguments(string[] pArgs, string[] pParams)
        {
            Dictionary<string, string> dict = new Dictionary<string, string>();

            foreach (string arg in pArgs)
            {
                foreach (string p in pParams)
                {
                    if (arg.StartsWith("-" + p + "="))
                    {
                        string a = arg.Substring(p.Length + 2, arg.Length - p.Length - 2);
                        if (a.StartsWith("\""))
                            a = a.Substring(1, a.Length - 2);

                        dict.Add(p, a);
                        goto next;
                    }
                }

                if (!dict.ContainsKey(""))
                {
                    string a = arg;
                    if (a.StartsWith("\""))
                        a = a.Substring(1, a.Length - 2);

                    dict.Add("", a);
                }

            next:
                continue;
            }

            return dict;
        }

        static int CharValue(int c)
        {
            if (c >= (int)'0' && c <= (int)'9')
                return (int)c - (int)'0';
            else if (c >= (int)'a' && c <= (int)'f')
                return 10 + ((int)c - (int)'a');
            else if (c >= (int)'A' && c <= (int)'F')
                return 10 + ((int)c - (int)'A');
            else
                return -1;
        }

        static void ParseIndices(string str, List<int> indices)
        {
            int i = 0;
            int step = 0;
            int firstChar = 0;
            for (; ; )
            {
                if (i >= str.Length)
                    break;

                int readNumber = 0;
                if (CharValue(str[i]) >= 0)
                {
                    readNumber = CharValue(str[i]);
                    i++;
                    int multiplier = 10;
                    if (i < str.Length && str[i] == 'x')
                    {
                        multiplier = 16;
                        i++;
                        readNumber = 0;
                    }

                    while (i < str.Length && CharValue(str[i]) >= 0)
                    {
                        readNumber *= multiplier;
                        readNumber += CharValue(str[i]);
                        i++;
                    }
                }
                else if (str[i] == '$')
                {
                    i++;
                    readNumber = (int)str[i];
                    i++;
                }
                else
                    throw new FormatException();

                if (step == 0)
                    firstChar = readNumber;

                if (step == 0 && i < str.Length && str[i] == '-')
                {
                    step = 1;
                    i++;
                }
                else if (i >= str.Length || str[i] == ',')
                {
                    step = 0;
                    if (firstChar > readNumber)
                        throw new FormatException();

                    for (int j = firstChar; j <= readNumber; j++)
                    {
                        indices.Add(j);
                    }

                    if (i < str.Length && str[i] == ',')
                        i++;
                }
                else
                    throw new FormatException();
            }
        }

        static void Main(string[] args)
        {
            Console.Out.WriteLine("GlyphExtract Command Line -- v1.3");
            Console.Out.WriteLine("Copyright 2015 Henrique Lorenzi");
            Console.Out.WriteLine("");
            
            var parameters = ParseArguments(args, new string[] { /*"xmlout",*/ "unicode", "size" });

            if (args.Length == 0)
            {
                Console.Out.WriteLine("Parameters:");
                Console.Out.WriteLine("\tunnamed:  The input font file.");
                //Console.Out.WriteLine("\t-xmlout:  The output XML file.");
                Console.Out.WriteLine("\t-size:    The size in which to render glyphs, in em units.");
                Console.Out.WriteLine("\t-unicode: The Unicode-mapped glyphs to extract, in the format: $a,$b-$z,32,33,34-40,0x1af,0x200-0x3ff");
            }
            else
            {
                try
                {
                    string ttfin = Path.GetFullPath(parameters[""]);

                    //string xmlout = Path.GetDirectoryName(ttfin) + Path.DirectorySeparatorChar + Path.GetFileNameWithoutExtension(ttfin) + "_glyphs.xml";
                    //if (parameters.ContainsKey("xmlout"))
                    //    xmlout = parameters["xmlout"];

                    double paramSize = 150;
                    if (parameters.ContainsKey("size"))
                        paramSize = Convert.ToDouble(parameters["size"]);

                    List<int> wantedUnicode = new List<int>();

                    if (parameters.ContainsKey("unicode"))
                        ParseIndices(parameters["unicode"], wantedUnicode);
                    else
                    {
                        for (int i = 0; i <= 127; i++)
                            wantedUnicode.Add(i);
                    }




                    GlyphTypeface glyphFace = new GlyphTypeface(new Uri("file:///" + ttfin));

                    var glyphMap = glyphFace.CharacterToGlyphMap;

                    List<GlyphData> result = new List<GlyphData>();

                    double fontSize = paramSize;// *(72.0 / 96.0);

                    double minY = 1e10;
                    double maxY = -1e10;

                    foreach (int wantedIndex in wantedUnicode)
                    {
                        try
                        {
                            Console.Out.Write("Extracting unicode 0x" + wantedIndex.ToString("X4") + "... ");
                            var glyphIndex = glyphMap[wantedIndex];
                            Console.Out.Write("from glyph 0x" + glyphIndex.ToString("X4") + "... ");

                            GlyphData data = new GlyphData();
                            data.glyphIndex = glyphIndex;
                            data.advanceWidth = (int)(fontSize * glyphFace.AdvanceWidths[glyphIndex]);

                            Geometry geometry = glyphFace.GetGlyphOutline(glyphIndex, fontSize, fontSize);
                            if (!geometry.IsEmpty())
                            {
                                DrawingVisual viz = new DrawingVisual();
                                using (DrawingContext dc = viz.RenderOpen())
                                {
                                    dc.DrawRectangle(System.Windows.Media.Brushes.Transparent, null, new System.Windows.Rect(-1000, -1000, 2000, 2000));

                                    double xOffset = (-geometry.Bounds.Left) + 1;
                                    double yOffset = (-geometry.Bounds.Top) + 1;
                                    dc.PushTransform(new TranslateTransform(xOffset, yOffset));
                                    dc.DrawGeometry(System.Windows.Media.Brushes.White, null, geometry);
                                    dc.Pop();
                                }

                                data.fileIndex = glyphIndex;
                                data.baseline = -(int)geometry.Bounds.Top;
                                data.xOffset = -(int)geometry.Bounds.Left;
                                data.width = (int)Math.Ceiling(geometry.Bounds.Width) + 2;
                                data.height = (int)Math.Ceiling(geometry.Bounds.Height) + 2;

                                if (-data.baseline < minY) minY = -data.baseline;
                                if (data.height - data.baseline > maxY) maxY = data.height - data.baseline;

                                RenderTargetBitmap bmp = new RenderTargetBitmap(
                                    (int)Math.Ceiling(geometry.Bounds.Width) + 2,
                                    (int)Math.Ceiling(geometry.Bounds.Height) + 2,
                                    96, 96, PixelFormats.Bgra32);
                                bmp.Render(viz);

                                var pngName = Path.GetDirectoryName(ttfin) + Path.DirectorySeparatorChar + "glyph" + glyphIndex.ToString("X4") + ".png";

                                PngBitmapEncoder encoder = new PngBitmapEncoder();
                                encoder.Frames.Add(BitmapFrame.Create(bmp));
                                using (FileStream file = new FileStream(pngName, FileMode.Create))
                                    encoder.Save(file);

                                using (FileStream glyphXml = new FileStream(Path.GetFileNameWithoutExtension(pngName) + ".sprsheet", FileMode.Create))
                                {
                                    WriteText(glyphXml, "<sprite-sheet src=\"" + Path.GetFileName(pngName) + "\">\n");
                                    WriteText(glyphXml, "\t<sprite ");
                                    WriteText(glyphXml, "name=\"" + glyphIndex.ToString("X4") + "\" ");
                                    WriteText(glyphXml, "x=\"0\" ");
                                    WriteText(glyphXml, "y=\"0\" ");
                                    WriteText(glyphXml, "width=\"" + data.width + "\" ");
                                    WriteText(glyphXml, "height=\"" + data.height + "\" ");
                                    WriteText(glyphXml, "crop-left=\"0\" ");
                                    WriteText(glyphXml, "crop-right=\"0\" ");
                                    WriteText(glyphXml, "crop-top=\"0\" ");
                                    WriteText(glyphXml, "crop-bottom=\"0\">\n");

                                    WriteText(glyphXml, "\t\t<guide name=\"unicode\" kind=\"int\" value=\"" + wantedIndex + "\"></guide>\n");
                                    WriteText(glyphXml, "\t\t<guide name=\"y-offset\" kind=\"int\" value=\"" + data.baseline + "\"></guide>\n");
                                    WriteText(glyphXml, "\t\t<guide name=\"x-offset\" kind=\"int\" value=\"" + data.xOffset + "\"></guide>\n");
                                    WriteText(glyphXml, "\t\t<guide name=\"x-advance\" kind=\"int\" value=\"" + data.advanceWidth + "\"></guide>\n");

                                    WriteText(glyphXml, "\t</sprite>\n");
                                    WriteText(glyphXml, "</sprite-sheet>\n");
                                }
                            }
                            else
                            {
                                data.fileIndex = -1;
                            }

                            result.Add(data);
                            Console.Out.WriteLine("Done.");
                        }
                        catch (KeyNotFoundException)
                        {
                            Console.Out.WriteLine("Not found.");
                        }
                    }

                    /*FileStream fileout = new FileStream(xmlout, FileMode.Create);

                    WriteText(fileout, "<document>\n");
                    WriteText(fileout, "\t<section version=\"1.0\" kind=\"fontmetrics\">\n");
                    WriteText(fileout, "\t\t<bounds ymin=\"" + (int)minY + "\" ymax=\"" + (int)maxY + "\" />\n");
                    WriteText(fileout, "\t</section>\n");

                    WriteText(fileout, "\t<section version=\"1.0\" kind=\"glyphmap\">\n");
                    foreach (int wantedChar in wantedUnicode)
                    {
                        try
                        {
                            WriteText(fileout, "\t\t<glyph index=\"" + glyphFace.CharacterToGlyphMap[wantedChar] + "\" unicode=\"" + wantedChar + "\" />\n");
                        }
                        catch (KeyNotFoundException)
                        {

                        }
                    }
                    WriteText(fileout, "\t</section>\n");

                    WriteText(fileout, "\t<section version=\"1.0\" kind=\"glyphmetrics\">\n");
                    foreach (GlyphData data in result)
                    {
                        WriteText(fileout,
                            "\t\t<glyph index=\"" + data.glyphIndex + "\" " +
                            "width=\"" + data.width + "\" height=\"" + data.height + "\" " +
                            "yoffset=\"" + data.baseline + "\" " +
                            "xoffset=\"" + data.xOffset + "\" " +
                            "xadvance=\"" + data.advanceWidth + "\" />\n");
                    }
                    WriteText(fileout, "\t</section>\n");

                    WriteText(fileout, "\t<section version=\"1.0\" kind=\"spritepacker\">\n");
                    //abspath=\"" + Path.GetDirectoryName(xmlout).Replace('\\', '/') + "/" + "\" >\n");
                    foreach (GlyphData data in result)
                    {
                        if (data.fileIndex == -1)
                            continue;

                        string filepath = Path.GetDirectoryName(ttfin) + Path.DirectorySeparatorChar + "glyph" + data.glyphIndex.ToString("X4") + ".png";
                        string path = Program.MakeRelativePath(xmlout, filepath).Replace('\\', '/');

                        WriteText(fileout,
                            "\t\t<sprite " +
                            "name=\"" + data.glyphIndex + "\" " +
                            "relpath=\"" + path + "\" " +
                            "width=\"" + data.width + "\" " +
                            "height=\"" + data.height + "\" " +
                            "/>\n");
                    }
                    WriteText(fileout, "\t</section>\n");
                    WriteText(fileout, "</document>");

                    fileout.Close();*/
                }
                catch (Exception e)
                {
                    Console.Out.WriteLine("An error has occurred.");
                    Console.Out.WriteLine(e.Message);
                    Console.Out.WriteLine(e.StackTrace);
                }
            }
        }
    }
}

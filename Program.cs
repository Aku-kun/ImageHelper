using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace ImageHelper
{
    class Program
    {
        static readonly string Root = @"D:\Users\Aku\Pictures";
        static readonly string Output = @"D:\Users\Aku\Pictures\PicturesSort";

        #region Option

        static readonly int MaxColorDifference = 10;
        static readonly int BlurSize = 2;
        static readonly int MinSize = 6;

        static readonly List<string> exps = new List<string>
        {
            ".jpg",
            ".JPG",
            ".jpeg",
            ".JPEG",
            ".png",
            ".PNG"
        };

        static readonly Dictionary<string, Func<Size, bool>> MainGroup = new Dictionary<string, Func<Size, bool>>
        {
            ["Vertical"] = s =>
            {
                return ((double)s.Width / s.Height) <= 0.9;
            },
            ["Square"] = s =>
            {
                double k = (double)s.Width / s.Height;
                return k > 0.9 && k < 1.1;
            },
            ["Horizontal"] = s =>
            {
                return ((double)s.Width / s.Height) >= 1.1;
            }
        };

        static readonly Dictionary<string, Func<Size, bool>> SubGroup = new Dictionary<string, Func<Size, bool>>
        {
            ["Low"] = s =>
            {
                return !SubGroup["High"].Invoke(s) && !SubGroup["Middle"].Invoke(s);
            },
            ["Middle"] = s =>
            {
                return !SubGroup["High"].Invoke(s)
                    ? s.Width >= 600 && s.Height >= 600
                    : false;
            },
            ["High"] = s =>
            {
                return (s.Width >= 1900 && s.Height >= 1000) || (s.Width >= 1000 && s.Height >= 1900);
            },
        };

        #endregion

        static void Main(string[] args)
        {
            DateTime start = DateTime.Now;

            List<string> images = GetAllImage(Root);
            Log($"Find {images.Count} image");

            if (images.Count == 0)
                Error("No image");
            else
            {
                Dictionary<string, List<string>> result = DeleteDuplicate(SortImage(images));
                SaveImage(result, Output);
            }

            TimeSpan date = DateTime.Now - start;
            string dateStr = $"{date.Hours}:{date.Minutes}:{date.Seconds}";
            Log($"Time: {dateStr}");

            Console.ReadKey();
        }

        #region Sort

        interface IBitmap : IDisposable
        {
            int Width { get; }
            int Height { get; }
            int ToARGB(int x, int y);
        }

        class MyBitmap : IBitmap
        {
            Bitmap bitmap;

            public MyBitmap(Bitmap bitmap) => this.bitmap = new Bitmap(bitmap);

            public int Width => bitmap.Width;
            public int Height => bitmap.Height;

            public int ToARGB(int x, int y)
            {
                Color color = bitmap.GetPixel(x, y);
                return (255 << 24) | (color.R << 16) | (color.G << 8) | color.B;
            }

            public void Dispose() => bitmap.Dispose();
        }

        unsafe class UnsafeBitmap : IBitmap
        {
            Bitmap bitmap;

            int width;
            BitmapData bitmapData = null;
            Byte* pBase = null;

            public UnsafeBitmap(Bitmap bitmap) 
                => this.bitmap = new Bitmap(bitmap);

            public int Width => bitmap.Width;
            public int Height => bitmap.Height;

            public void LockBitmap()
            {
                GraphicsUnit unit = GraphicsUnit.Pixel;
                RectangleF boundsF = bitmap.GetBounds(ref unit);
                Rectangle bounds = new Rectangle((int)boundsF.X,
                (int)boundsF.Y,
                (int)boundsF.Width,
                (int)boundsF.Height);

                width = (int)boundsF.Width * sizeof(PixelData);
                if (width % 4 != 0)
                {
                    width = 4 * (width / 4 + 1);
                }
                bitmapData = bitmap.LockBits(bounds, ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);

                pBase = (Byte*)bitmapData.Scan0.ToPointer();
            }

            public int ToARGB(int x, int y)
                => GetPixel(x, y).ToARGB();

            public PixelData GetPixel(int x, int y)
            {
                PixelData returnValue = *PixelAt(x, y);
                return returnValue;
            }

            public void UnlockBitmap()
            {
                bitmap.UnlockBits(bitmapData);
                bitmapData = null;
                pBase = null;
            }

            public PixelData* PixelAt(int x, int y) 
                => (PixelData*)(pBase + y * width + x * sizeof(PixelData));


            public void Dispose()
                => bitmap.Dispose();
        }

        struct PixelData
        {
            public byte blue;
            public byte green;
            public byte red;

            public int ToARGB()
                => (255 << 24) | (red << 16) | (green << 8) | blue;
        }

        struct Img
        {
            public string Path;
            public Size Size;
            public int Color;

            public Img(string path)
            {
                string name = path.Substring(path.LastIndexOf('\\') + 1);
                Log($"Start process image {name}", ConsoleColor.DarkGray);

                Path = path;

                string unSafe = "[unsafe method]";
                using (Image img = Image.FromFile(path))
                {
                    Size = img.Size;

                    int size = MinSize + BlurSize * 2;
                    if (img.Width < size || img.Height < size)
                        throw new Exception($"Image must be > {size}x{size}");

                    using (Bitmap bmpR = new Bitmap(img, new Size(img.Width / MinSize, img.Height / MinSize)))
                    {
                        try
                        {
                            using (UnsafeBitmap bmp = new UnsafeBitmap(bmpR))
                            {
                                bmp.LockBitmap();
                                Color = GetColor(bmp);
                                bmp.UnlockBitmap();
                            }
                        }
                        catch
                        {
                            unSafe = "[safe method]";
                            using (MyBitmap bmp = new MyBitmap(bmpR))
                            {
                                Color = GetColor(bmp);
                            }
                        }
                    }
                }

                Color color = System.Drawing.Color.FromArgb(Color);
                Log($"End {unSafe} process image {name} | Size [{Size.Width}x{Size.Height}] | {color}", ConsoleColor.DarkGray);
            }

            static int GetColor(IBitmap bmp)
            {
                Dictionary<int, int> list = new Dictionary<int, int>();

                int bluerMatrix = (BlurSize * 2) * (BlurSize * 2);
                for (int x = BlurSize; x < bmp.Width - BlurSize; x++)
                    for (int y = BlurSize; y < bmp.Height - BlurSize; y++)
                    {
                        int rgb = 0;
                        for (int i = x - BlurSize; i < x + BlurSize; i++)
                            for (int j = y - BlurSize; j < y + BlurSize; j++)
                                rgb += bmp.ToARGB(x, y);

                        rgb /= bluerMatrix;

                        if (rgb > -15000000 && rgb < -8000000)
                        {
                            bool added = false;
                            for (int i = 0; i <= MaxColorDifference; i++)
                            {
                                if (list.ContainsKey(rgb + i))
                                {
                                    list[rgb + i] += 1;
                                    added = true;
                                    break;
                                }
                                if (list.ContainsKey(rgb - i))
                                {
                                    list[rgb - i] += 1;
                                    added = true;
                                    break;
                                }
                            }
                            if (added == false)
                                list.Add(rgb, 1);
                        }
                    }

                int maxV = list.Values.Max();
                return list.First(l => l.Value == maxV).Key;
            }
        }

        static Dictionary<string, List<string>> SortImage(List<string> root)
        {
            Log($"Start load image info");
            List<Img> imagesInfo = new List<Img>();

            foreach (string path in root)
            {
                try
                {
                    imagesInfo.Add(new Img(path));
                }
                catch (Exception e)
                {
                    Error($"Load image {path}", e);
                }
            }
            root = null;

            Log($"Start sort image by size");
            Dictionary<string, List<Img>> group = new Dictionary<string, List<Img>>();

            foreach (string key in MainGroup.Keys)
            {
                List<Img> images = new List<Img>();

                foreach (Img image in imagesInfo)
                {
                    if (MainGroup[key].Invoke(image.Size))
                        images.Add(image);
                }

                foreach (Img img in images)
                    imagesInfo.Remove(img);

                if (images.Count == 0)
                    Log($"No image in group {key}", ConsoleColor.Yellow);
                else
                {
                    Log($"Group {key} contains {images.Count} image", ConsoleColor.Green);
                    group.Add(key, images);
                }
            }

            if (imagesInfo.Count != 0)
                Error($"{imagesInfo.Count} image can't sort");


            Log($"Start sort image by size & color in group");
            Dictionary<string, List<string>> result = new Dictionary<string, List<string>>();
            foreach (string key in group.Keys)
            {
                foreach (string subKey in SubGroup.Keys)
                {
                    string newKey = $@"{key}\{subKey}";
                    List<Img> images = new List<Img>();

                    foreach (Img img in group[key])
                    {
                        if (SubGroup[subKey].Invoke(img.Size))
                            images.Add(img);
                    }

                    foreach (Img img in images)
                        group[key].Remove(img);

                    if (images.Count == 0)
                        Log($"No image in group {newKey}", ConsoleColor.Yellow);
                    else
                    {
                        Log($"Group {newKey} contains {images.Count} image", ConsoleColor.Green);
                        result.Add(newKey, images.OrderBy(i => i.Color).Select(img => img.Path).ToList());
                    }
                }

                if (group[key].Count != 0)
                    Error($"{group[key].Count} image can't sort in group {key}");
            }

            return result;
        }

        static List<string> GetAllImage(string root)
        {
            Log($"Start search in folder {root}");
            List<string> result = new List<string>();

            foreach (string path in Directory.GetDirectories(root))
                result.AddRange(GetAllImage(path));

            foreach (string path in Directory.GetFiles(root))
            {
                string exp = Path.GetExtension(path);
                if (exps.Contains(exp))
                    result.Add(path);
            }

            return result;
        }

        #endregion
        #region Save 

        static string GetHash(string path)
        {
            using (FileStream fileStream = new FileStream(path, FileMode.Open, FileAccess.Read))
            {
                return Encoding.UTF8.GetString(new SHA1Managed().ComputeHash(fileStream));
            }
        }

        static Dictionary<string, List<string>> DeleteDuplicate(Dictionary<string, List<string>> images)
        {
            foreach (string key in images.Keys)
            {
                Log($"Start search duplicate image in group {key}");

                List<string> duplicates = images[key].Select(f => new
                {
                    FileName = f,
                    FileHash = GetHash(f)
                }).GroupBy(f => f.FileHash)
                .Select(g => new { FileHash = g.Key, Files = g.Select(z => z.FileName).ToList() })
                .SelectMany(f => f.Files.Skip(1))
                .ToList();

                if (duplicates.Count > 0)
                {
                    Log($"Find {duplicates.Count} duplicate image in group {key}", ConsoleColor.Yellow);
                    foreach (string path in duplicates)
                        images[key].Remove(path);

                    Log($"Delete all duplicate image in group {key}", ConsoleColor.Green);
                }
            }

            Log($"Delete all duplicate", ConsoleColor.Green);
            return images;
        }

        static void SaveImage(Dictionary<string, List<string>> images, string path)
        {
            foreach (string key in images.Keys)
            {
                Log($"Start save image in group {key}");
                string newPath = Path.Combine(path, key);
                if (!Directory.Exists(newPath))
                    Directory.CreateDirectory(newPath);

                int count = 0;
                foreach (string image in images[key])
                {
                    string name = $"Art{count}{Path.GetExtension(image)}";
                    try
                    {
                        File.Copy(image, Path.Combine(newPath, name));
                        count++;
                    }
                    catch (Exception e)
                    {
                        Error($"Copy image {image}", e);
                    }
                }

                if (count == images[key].Count)
                    Log($"Save all image in group {key}", ConsoleColor.Green);
                else
                    Log($"Save {count}/{images[key].Count} image in group {key}", ConsoleColor.Yellow);
            }

            Log($"Save all image", ConsoleColor.Green);
        }

        #endregion
        #region Log

        static void Log(string message, ConsoleColor? color = null)
        {
            DateTime date = DateTime.Now;
            string dateStr = $"{date.Hour}:{date.Minute}:{date.Second}";
            Console.Write($"[{dateStr}]: ");

            if (color.HasValue)
                Console.ForegroundColor = color.Value;

            Console.WriteLine(message);

            if (color.HasValue)
                Console.ResetColor();
        }

        static void Error(string message, Exception ex = null)
        {
            DateTime date = DateTime.Now;
            string dateStr = $"{date.Hour}:{date.Minute}:{date.Second}";
            Console.Write($"[{dateStr}]: ");

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(message);

            if (ex != null)
                Console.WriteLine($" {"".PadLeft(dateStr.Length, ' ')}   {ex.Message}");

            Console.ResetColor();
        }

        #endregion
    }
}

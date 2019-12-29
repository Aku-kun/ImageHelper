using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace ImageHelper
{
    class Program
    {
        static readonly string Root = @"D:\Users\Aku\Pictures\1";
        static readonly string Output = @"D:\Users\Aku\Pictures\1Sort";

        #region Option

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
                return s.Width < 600 && s.Height < 600;
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
                SaveImage(SortImage(images), Output);
                DeleteDuplicate(Output);
                Log($"Complite", ConsoleColor.Green);
            }

            TimeSpan date = DateTime.Now - start;
            string dateStr = $"{date.Hours}:{date.Minutes}:{date.Seconds}";
            Log($"Time: {dateStr}");

            Console.ReadKey();
        }

        #region Sort

        struct Img
        {
            public string Path;
            public Size Size;
            public float Color;

            public Img(string path)
            {
                Path = path;
                using (Image img = Image.FromFile(path))
                {
                    Size = img.Size;

                    Log($"Process image {path.Substring(path.LastIndexOf('\\') + 1)}", ConsoleColor.DarkGray);

                    using (Bitmap bmp = new Bitmap(img, new Size(img.Width / 4, img.Height / 4)))
                    {
                        Dictionary<int, int> list = new Dictionary<int, int>();

                        for (int x = 0; x < bmp.Width; x++)
                            for (int y = 0; y < bmp.Height; y++)
                            {
                                int h = (int)Math.Round(bmp.GetPixel(x, y).GetHue());

                                bool added = false;
                                for (int i = 0; i < 10; i++)
                                {
                                    if (list.ContainsKey(h + i))
                                    {
                                        list[h + i]++;
                                        added = true;
                                        break;
                                    }
                                    if (list.ContainsKey(h - i))
                                    {
                                        list[h - i]++;
                                        added = true;
                                        break;
                                    }
                                }
                                if (!added)
                                    list.Add(h, 1);

                            }

                        Color = list.First(x => x.Value == list.Values.Max()).Key;
                    }
                }
            }
        }

        static Dictionary<string, List<string>> SortImage(List<string> root)
        {
            Log($"Start sort image by size");
            Dictionary<string, List<Img>> group = new Dictionary<string, List<Img>>();

            foreach (string key in MainGroup.Keys)
            {
                List<Img> images = new List<Img>();

                foreach (string path in root)
                {
                    try
                    {
                        Img image = new Img(path);
                        if (MainGroup[key].Invoke(image.Size))
                            images.Add(image);
                    }
                    catch (Exception e)
                    {
                        Error($"Load image {path}", e);
                    }
                }

                foreach (Img img in images)
                    root.Remove(img.Path);

                Log($"Group {key} contains {images.Count} image", images.Count == 0 ? (ConsoleColor?)null : ConsoleColor.Green);

                if (images.Count == 0)
                    Log($"No image in group {key}", ConsoleColor.Yellow);
                else
                    group.Add(key, images);
            }

            if (root.Count != 0)
                Error($"Any image can't sort {root.Count}");


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


                    Log($"Group {newKey} contains {images.Count} image", images.Count == 0 ? (ConsoleColor?)null : ConsoleColor.Green);

                    if (images.Count == 0)
                        Log($"No image in group {newKey}", ConsoleColor.Yellow);
                    else
                        result.Add(newKey, images.OrderBy(i => i.Color).Select(img => img.Path).ToList());
                }
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

        static void DeleteDuplicate(string root)
        {
            foreach (string path in Directory.GetDirectories(root))
                DeleteDuplicate(path);

            List<string> duplicates = Directory.GetFiles(root).Select(f => new
            {
                FileName = f,
                FileHash = GetHash(f)
            }).GroupBy(f => f.FileHash)
                .Select(g => new { FileHash = g.Key, Files = g.Select(z => z.FileName).ToList() })
                .SelectMany(f => f.Files.Skip(1))
                .ToList();

            if (duplicates.Count > 0)
            {
                Log($"Find {duplicates.Count} duplicate image in folder {root}", ConsoleColor.Yellow);
                foreach (string path in duplicates)
                {
                    try
                    {
                        File.Delete(path);
                    }
                    catch (Exception e)
                    {
                        Error($"Delete duplicate image {path}", e);
                    }
                }

                Log($"Delete all duplicate image in folder {root}", ConsoleColor.Green);
            }
        }

        static void SaveImage(Dictionary<string, List<string>> images, string path)
        {
            Log($"Start save image");
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
            }
            Log($"Complite save image", ConsoleColor.Green);
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

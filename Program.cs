using System;
using System.IO;
using System.Collections.Generic;

using ImageMagick;
using System.Net;
using ImageMagick.Formats.Jpeg;
using ImageMagick.ImageOptimizers;
using ImageMagick.Formats.Png;

namespace SpritePacker
{
	class Program
	{
        const int MAX_SIZE = 256;

        public class ImageEx
		{
            public string Path;
            public MagickImage Image;
            public int AtlasX;
            public int AtlasY;
		}

        public static List<string> EnumerateFilesRecursive(string directory, string fileExtension)
		{
            List<string> results = new List<string>();

            try
            {
                List<string> files = new List<string>(Directory.EnumerateFiles(directory));

                foreach(var file in files)
				{
                    if (file.EndsWith(fileExtension))
                        results.Add(file);
				}

                List<string> dirs = new List<string>(Directory.EnumerateDirectories(directory));

                foreach (var dir in dirs)
                    results.AddRange(EnumerateFilesRecursive(dir, fileExtension));
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.WriteLine(ex.Message);
            }
            catch (PathTooLongException ex)
            {
                Console.WriteLine(ex.Message);
            }

            return results;
        }

        public static List<ImageEx> LoadBitmaps(List<string> files, int maxSize)
		{
            List<ImageEx> images = new List<ImageEx>();

            foreach (var file in files)
            {
                ImageEx img = new ImageEx();

                img.Path = file;

                try 
                { 
                    img.Image = new MagickImage(file);

                    if (img.Image.Width < maxSize && img.Image.Height < maxSize)
                    {
                        if (img.Image.BitDepth() == 8)
                            images.Add(img);
                        else
                            Console.WriteLine($"Skip: {img.Path} Reason: Not 8 bpp");
                    }
                    else
					{
                        Console.WriteLine($"Skip: {img.Path} Reason: File too big {img.Image.Width}x{img.Image.Height}");
                    }
                }
                catch(Exception e)
				{
                    Console.WriteLine($"Error: {file} Reason: {e.Message}");
				}

                if (images.Count > 10)
                    break;
            }

            images.Sort((ImageEx a, ImageEx b) => 
            {
                if (a.Image.Width * a.Image.Height > b.Image.Width * b.Image.Height)
                    return -1;
                else
                    return 1;
            });

            return images;
		}

        public static MagickColor[] GetColorMap(string file)
		{
            var colorMap = new MagickColor[256];
            var image = new MagickImage(file);

            for (int i = 0; i < 256; i++)
                colorMap[i] = new MagickColor(image.GetColormap(i));

            return colorMap;
		}
        public static bool[,] ResizeMask(bool [,] mask, int newWidth, int newHeight)
		{
            Console.WriteLine($"Resize: {newWidth}x{newHeight}");

            bool[,] newMask = new bool[newWidth, newHeight];
            for (int mx = 0; mx < mask.GetUpperBound(0); mx++)
                for (int my = 0; my < mask.GetUpperBound(1); my++)
                    newMask[mx, my] = mask[mx, my];
            return newMask;
        }

        // Simple algorithm from here: https://gamedev.stackexchange.com/questions/2829/texture-packing-algorithm
        public static (int width, int height) Resolve(ref List<ImageEx> images)
		{
            int totalArea = 0;

            foreach (var bitmap in images)
                totalArea += bitmap.Image.Width * bitmap.Image.Height;

            int desiredWidth = (int)Math.Sqrt(totalArea);

            int maxWidth = 0;
            foreach (var image in images)
                if (image.Image.Width > maxWidth)
                    maxWidth = image.Image.Width;

            if (maxWidth > desiredWidth)
                desiredWidth = maxWidth;

            int atlasWidth = 2;
            while (atlasWidth < desiredWidth)
                atlasWidth *= 2;

            int atlasHeight = atlasWidth;

            bool[,] mask = new bool[atlasWidth, atlasHeight];

            for (int i = 0; i < images.Count; i++)
			{
                ImageEx img = images[i];
                int ix = img.Image.Width;
                int iy = img.Image.Height;

                bool canFit = true;

                int atlasX = 0;
                int atlasY = 0;

                for (int y = 0; y < atlasHeight; y++)
				{
                    if (y + img.Image.Height >= atlasHeight)
                    {
                        atlasHeight += images[i].Image.Height;
                        mask = ResizeMask(mask, atlasWidth, atlasHeight);
                    }

                    for (int x = 0; x < atlasWidth - ix; x++)
					{
                        canFit = true;
                        for (int mx = x; mx < x + ix && mx < atlasWidth && canFit; mx++)
                            for (int my = y; my < y + iy && my < atlasHeight && canFit; my++)
                                if (mask[mx, my])
                                    canFit = false;

                        if (canFit)
						{
                            atlasX = x;
                            atlasY = y;
                            break;
						}
                    }

                    if (canFit)
                        break;
                }
                if (canFit)
                {
                    Console.WriteLine($"{img.Path} {atlasX}x{atlasY}");

                    img.AtlasX = atlasX;
                    img.AtlasY = atlasY;

                    for (int mx = atlasX; mx < atlasX + ix; mx++)
                        for (int my = atlasY; my < atlasY + iy; my++)
                            mask[mx, my] = true;
                }
                else
				{
                    Console.WriteLine($"Can't fit {img.Path} {img.Image.Width}x{img.Image.Height}");
				}
			}

            return (atlasWidth, atlasHeight);
		}

        public static void Save(string directory, (int width, int height) size, MagickColor[] colorMap, List<ImageEx> images)
		{
            var outputBitmap = new MagickImage();
            outputBitmap.PreserveColorType();
            outputBitmap.Read("z:\\Git\\SpritePacker\\palette.bmp");
            outputBitmap.Format = MagickFormat.Png8;
            outputBitmap.Resize(size.width, size.height);

            outputBitmap.ColormapSize = 256;
            for (int i = 0; i < 256; i++)
                outputBitmap.SetColormap(i, colorMap[i]);

            foreach (var image in images)
                outputBitmap.Composite(image.Image, new PointD(image.AtlasX, image.AtlasY));

            outputBitmap.Settings.SetDefine(MagickFormat.Png8, "preserve-colormap", "true");

            outputBitmap.Write(directory + "/SpriteAtlas.png");
		}

        static void Main(string[] args)
		{
			string rootDirectory = ".";

			if (args.Length > 0)
				rootDirectory = args[0];

            var files = EnumerateFilesRecursive(rootDirectory, ".bmp");

            if (files.Count == 0)
			{
                Console.WriteLine("No files to process.");
                return;
			}

            var colorMap = GetColorMap("z:\\Git\\SpritePacker\\palette.bmp");

            Console.WriteLine("Loading...");
            var images = LoadBitmaps(files, MAX_SIZE);

            Console.WriteLine("Fitting...");
            var size = Resolve(ref images);

            Console.WriteLine("Merging...");
            Save(rootDirectory, size, colorMap, images);

            //Console.WriteLine($"Total area: {totalArea}");
        }
	}
}

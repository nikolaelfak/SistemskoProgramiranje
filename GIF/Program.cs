using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Runtime.Caching;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Transforms;
using SixLabors.ImageSharp.Formats.Gif;
using System.Linq;

class Program
{
    static readonly MemoryCache cache = new MemoryCache("ImageCache");
    static readonly object cacheLock = new object();

    static void Main(string[] args)
    {
        Console.WriteLine("Starting server...");

        var listener = new HttpListener();
        listener.Prefixes.Add("http://localhost:5050/");
        listener.Start();

        while (true)
        {
            try
            {
                var context = listener.GetContext();
                var thread = new Thread(() => HandleRequest(context));
                thread.Start();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception: {ex.Message}");
            }
        }
    }

    static void HandleRequest(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        if (request.HttpMethod == "GET" && request.QueryString.HasKeys())
        {
            var filename = request.QueryString.GetValues(0)[0];

            // podrzane ekstenzije
            string[] allowedExtensions = { ".jpg", ".png", ".gif" };

            // Proveravamo da li je prosledjeni fajl validan i da li ima podrzanu ekstenziju
            if (!File.Exists(filename) || !allowedExtensions.Contains(Path.GetExtension(filename).ToLower()))
            {
                response.StatusCode = (int)HttpStatusCode.BadRequest;
                response.StatusDescription = "Bad request";
                response.Close();
                Console.WriteLine($"Invalid request for {filename}");
                return;
            }

            byte[] result;
            lock (cacheLock)
            {
                if (cache.Contains(filename))
                {
                    Console.WriteLine($"Cache hit for {filename}");
                    result = (byte[])cache.Get(filename);
                }
                else
                {
                    Console.WriteLine($"Processing request for {filename}");
                    try
                    {
                        var file = File.ReadAllBytes(filename);

                        using var image = Image.Load(file);
                        using var imageStream = new MemoryStream();
                        var gifEncoder = new GifEncoder();

                        // Generišemo niz slučajnih boja koje ćemo koristiti za svaki frejm
                        var random = new Random();
                        var colors = new Rgba32[10];
                        for (int i = 0; i < colors.Length; i++)
                        {
                            colors[i] = new Rgba32(
                                (byte)random.Next(256),
                                (byte)random.Next(256),
                                (byte)random.Next(256)
                            );
                        }

                        for (int i = 0; i < 10; i++)
                        {
                            // Koristimo sledeću boju iz niza boja
                            var color = colors[i];
                            image.Mutate(x => x
                                .Colorize(color)
                                .Resize(new ResizeOptions { Size = image.Size() })
                            );
                            gifEncoder.AddFrame(image.Frames.RootFrame);
                        }

                        result = imageStream.ToArray();
                        cache.Set(filename, result, new CacheItemPolicy { SlidingExpiration = TimeSpan.FromMinutes(5) });
                    }
                    catch (Exception ex)
                    {
                        response.StatusCode = (int)HttpStatusCode.BadRequest;
                        response.StatusDescription = "Bad request";
                        response.Close();
                        Console.WriteLine($"Error processing request for {filename}: {ex.Message}");
                        return;
                    }
                }
            }

            response.ContentType = "image/gif";
            response.ContentLength64 = result.Length;
            response.OutputStream.Write(result, 0, result.Length);
            response.OutputStream.Close();
        }
        else
        {
            response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
            response.StatusDescription = "Method not allowed";
            response.Close();
        }
    }
}

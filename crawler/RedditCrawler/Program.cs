﻿using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using RedditSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace StereoscopyVR.RedditCrawler
{
    class Program
    {
        const string SaveLocation = "out";
        const string DownloadLocation = "drop";
        const string PostsFile = "posts.json";
        static public IConfigurationRoot Configuration { get; set; }
        static void Main(string[] args)
        {
            Directory.CreateDirectory(SaveLocation);
            Directory.CreateDirectory(DownloadLocation);

            Configure();
            MainAsync().Wait();
        }

        private static async Task MainAsync()
        {
            Console.WriteLine("Do you want to download new posts? (y, enter)");
            IEnumerable<CrossViewPost> posts = null;
            if (Console.ReadLine().ToLowerInvariant() == "y")
            {
                Console.WriteLine("Downloading data from Reddit...");
                posts = await GetPosts();
                Console.WriteLine("Writing data to disk...");
                using (StreamWriter file = File.CreateText(Path.Combine(DownloadLocation, PostsFile)))
                {
                    var serializer = new JsonSerializer();
                    var collection = new SceneCollection() { scenes = posts };
                    serializer.Serialize(file, collection);
                }
            }
            else
            {
                Console.WriteLine("Reading data from disk...");
                using (StreamReader file = File.OpenText(Path.Combine(DownloadLocation, PostsFile)))
                {
                    var serializer = new JsonSerializer();
                    var collection = (SceneCollection)serializer.Deserialize(file, typeof(SceneCollection));
                    posts = collection.scenes;
                }
            }

            Console.WriteLine("Fetching image URLs");
            await GetImageUrls(posts);

            Console.WriteLine("Do you want to process images? (y, enter)");
            if (Console.ReadLine().ToLowerInvariant() == "y")
            {
                var goodPosts = await DoWork(posts);
                using (StreamWriter file = File.CreateText(Path.Combine(SaveLocation, PostsFile)))
                {
                    var serializer = new JsonSerializer();
                    var collection = new SceneCollection() { scenes = posts };
                    serializer.Serialize(file, collection);
                }
            }

            Console.WriteLine("All done. Hit Enter to exit.");
            Console.ReadLine();
        }

        private static async Task GetImageUrls(IEnumerable<CrossViewPost> posts)
        {
            foreach (var post in posts)
            {
                try
                {
                    await post.TryGetImageUrl();
                    if (post.ImageUrl != null)
                    {
                        Console.WriteLine($"OK: [{post.Score}] {post.Title}");
                    }
                    else
                    {
                        Console.WriteLine($"Not supported domain {post.Url.Host}: [{post.Score}] {post.Title}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error at {post.Url.Host}: {ex}");
                }
            }
        }

        private static async Task GetAndSavePosts()
        {
            var posts = await GetPosts();

        }

        private static async Task<IEnumerable<CrossViewPost>> DoWork(IEnumerable<CrossViewPost> posts)
        {
            List<CrossViewPost> goodPosts = new List<CrossViewPost>();
            foreach (var post in posts.Where(n => n.ImageUrl != null))
            {
                var filePath = Path.Combine(DownloadLocation, post.Link + ".img");
                await DownloadAsync(post, filePath);
                try
                {
                    ImageApp.Program.ProcessFile(filePath);
                    goodPosts.Add(post);
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Error processing {filePath}: {e.Message}");
                }
            }
            return goodPosts;
        }

        private static async Task DownloadAsync(CrossViewPost post, string filePath)
        {
            using (var client = new HttpClient())
            {
                var request = new HttpRequestMessage(HttpMethod.Get, post.ImageUrl);
                using (var response = await client.SendAsync(request))
                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    var dataStream = await response.Content.ReadAsStreamAsync();
                    await dataStream.CopyToAsync(fileStream);
                }
            }
        }

        private static void Configure()
        {
            var configPath = Environment.ExpandEnvironmentVariables(@"%USERPROFILE%\OneDrive\current\vrcv.json");
            var builder = new ConfigurationBuilder()
                .AddJsonFile(configPath);
            Configuration = builder.Build();
        }

        private static async Task<IEnumerable<CrossViewPost>> GetPosts()
        {
            var webAgent = new BotWebAgent(Configuration["reddit-username"], Configuration["reddit-password"], Configuration["reddit-token"], Configuration["reddit-secret"], Configuration["reddit-url"]);
            //This will check if the access token is about to expire before each request and automatically request a new one for you
            //"false" means that it will NOT load the logged in user profile so reddit.User will be null
            var reddit = new Reddit(webAgent, false);
            await reddit.InitOrUpdateUserAsync();
            var authenticated = reddit.User != null;
            if (!authenticated)
                Console.WriteLine("Invalid token");

            var subreddit = await reddit.GetSubredditAsync("/r/crossview");
            var posts = new List<CrossViewPost>();
            await subreddit.GetTop(RedditSharp.Things.FromTime.Month).Take(50).ForEachAsync(post => {
                var data = new CrossViewPost(post.Url, post.Title, post.Shortlink, post.Score, post.CreatedUTC);
                Console.WriteLine(post.Title);
                posts.Add(data);
            });
            return posts;
        }
    }
}
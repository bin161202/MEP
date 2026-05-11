using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MEPAuto.Server.Core.Models;
using MEPAuto.Server.Infrastructure.FileSystem;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;

namespace MEPAuto.Server.Api.Commands
{
    /// <summary>
    /// CLI subcommand cho dev local seed user vào DataDir mà không cần SSH VPS.
    /// Chạy: dotnet run -- seed-user --email X --password Y [--features f1,f2] [--display "Tên"] [--data-dir path]
    /// Ghi vào DataDir local (Development = ./data-dev). KHÔNG động vào VPS production.
    /// </summary>
    public static class SeedUserCommand
    {
        public static async Task<int> RunAsync(string[] args)
        {
            string? email = null;
            string? password = null;
            string? display = null;
            string? featuresCsv = null;
            string? dataDirOverride = null;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--email" when i + 1 < args.Length: email = args[++i]; break;
                    case "--password" when i + 1 < args.Length: password = args[++i]; break;
                    case "--display" when i + 1 < args.Length: display = args[++i]; break;
                    case "--features" when i + 1 < args.Length: featuresCsv = args[++i]; break;
                    case "--data-dir" when i + 1 < args.Length: dataDirOverride = args[++i]; break;
                    case "-h":
                    case "--help":
                        PrintUsage();
                        return 0;
                    default:
                        Console.Error.WriteLine($"ERR: argument không hợp lệ '{args[i]}'");
                        PrintUsage();
                        return 2;
                }
            }

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                Console.Error.WriteLine("ERR: --email và --password bắt buộc.");
                PrintUsage();
                return 2;
            }

            var dataDir = ResolveDataDir(dataDirOverride);
            Directory.CreateDirectory(dataDir);

            var usersPath = Path.Combine(dataDir, "users.json");
            var licensesPath = Path.Combine(dataDir, "licenses.json");

            var users = await LoadUsers(usersPath);
            var licenses = await LoadLicenses(licensesPath);

            var existing = users.FirstOrDefault(u =>
                string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase));
            var hash = BCryptPasswordHasher.Hash(password);
            string userId;
            string action;

            if (existing != null)
            {
                existing.PasswordHash = hash;
                if (!string.IsNullOrWhiteSpace(display)) existing.DisplayName = display;
                existing.Disabled = false;
                userId = existing.UserId;
                action = "UPDATED";
            }
            else
            {
                userId = "u-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                users.Add(new User
                {
                    UserId = userId,
                    Email = email!,
                    PasswordHash = hash,
                    DisplayName = display ?? "",
                    Disabled = false,
                    CreatedAt = DateTime.UtcNow,
                    LastLoginAt = null,
                });
                action = "CREATED";
            }

            var features = ParseFeatures(featuresCsv);
            if (features.Count > 0)
            {
                licenses[email!] = features;
            }

            AtomicWriteJson(usersPath, users);
            AtomicWriteJson(licensesPath, licenses);

            Console.WriteLine($"{action} user {email} (UserId={userId})");
            if (features.Count > 0)
                Console.WriteLine($"LICENSE {email} → [{string.Join(", ", features)}]");
            else
                Console.WriteLine($"LICENSE {email} → (không đổi)");
            Console.WriteLine($"OK — wrote to {Path.GetFullPath(dataDir)}");
            return 0;
        }

        private static string ResolveDataDir(string? overridePath)
        {
            if (!string.IsNullOrWhiteSpace(overridePath)) return overridePath!;

            var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
            var cfg = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true)
                .AddJsonFile($"appsettings.{env}.json", optional: true)
                .AddEnvironmentVariables()
                .Build();
            return cfg["DataDir"] ?? "./data-dev";
        }

        private static async Task<List<User>> LoadUsers(string path)
        {
            if (!File.Exists(path)) return new List<User>();
            var json = await File.ReadAllTextAsync(path);
            if (string.IsNullOrWhiteSpace(json)) return new List<User>();
            return JsonConvert.DeserializeObject<List<User>>(json) ?? new List<User>();
        }

        private static async Task<Dictionary<string, List<string>>> LoadLicenses(string path)
        {
            if (!File.Exists(path))
                return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var json = await File.ReadAllTextAsync(path);
            if (string.IsNullOrWhiteSpace(json))
                return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var dict = JsonConvert.DeserializeObject<Dictionary<string, List<string>>>(json)
                       ?? new Dictionary<string, List<string>>();
            return new Dictionary<string, List<string>>(dict, StringComparer.OrdinalIgnoreCase);
        }

        private static List<string> ParseFeatures(string? csv)
        {
            if (string.IsNullOrWhiteSpace(csv)) return new List<string>();
            return csv.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static void AtomicWriteJson<T>(string path, T data)
        {
            var dir = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(dir);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(data, Formatting.Indented));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        private static void PrintUsage()
        {
            Console.WriteLine(@"
Usage:
  dotnet run -- seed-user --email <X> --password <Y> [options]

Required:
  --email <email>            Email user dev (vd minhduy@local.dev)
  --password <plaintext>     Password — sẽ hash BCrypt cost=11

Options:
  --features <csv>           License keys cách nhau dấu phẩy
                             vd 'helloworld.basic,toiletstackconnect.basic'
  --display <name>           DisplayName trong users.json
  --data-dir <path>          Override DataDir
                             (default: theo appsettings; Development = ./data-dev)

Ghi vào <data-dir>/users.json + licenses.json LOCAL trên máy.
KHÔNG ảnh hưởng VPS production.

Ví dụ:
  $env:ASPNETCORE_ENVIRONMENT='Development'
  dotnet run --project src/server/MEPAuto.Server.Api -- seed-user `
      --email minhduy@local.dev --password Test123 `
      --features helloworld.basic,toiletstackconnect.basic `
      --display 'Minh Duy (dev)'
");
        }
    }
}

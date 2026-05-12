// Tiny CLI: in plain password → out BCrypt hash workfactor 11 (matching server BCryptPasswordHasher).
// Mục đích: bootstrap-local-server.ps1 chạy trên PS 5.1 không load được BCrypt.Net-Next.dll net8.0
// (Add-Type ReflectionTypeLoadException) → delegate hashing sang dotnet net8 process này.
using System;

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: HashPwd <password> [workFactor=11]");
    Environment.Exit(2);
}
int wf = args.Length >= 2 && int.TryParse(args[1], out var w) ? w : 11;
Console.Write(BCrypt.Net.BCrypt.HashPassword(args[0], wf));
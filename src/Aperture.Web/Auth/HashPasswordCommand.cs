namespace Aperture.Web.Auth;

/// <summary>
/// <c>dotnet run --project src/Aperture.Web -- hash-password</c>: asks for a username and password
/// and prints the settings to configure, so the plain password is never stored anywhere.
/// </summary>
public static class HashPasswordCommand
{
    public static int Run()
    {
        Console.Write("Username: ");
        var username = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(username))
        {
            Console.Error.WriteLine("Username must not be empty.");
            return 1;
        }

        var password = ReadHidden("Password: ");
        if (password.Length < 8)
        {
            Console.Error.WriteLine("Use at least 8 characters.");
            return 1;
        }

        if (ReadHidden("Repeat password: ") != password)
        {
            Console.Error.WriteLine("Passwords do not match.");
            return 1;
        }

        var hash = CredentialValidator.HashPassword(password);
        Console.WriteLine();
        Console.WriteLine("Development (stored in your user profile, outside the repo):");
        Console.WriteLine($"  dotnet user-secrets --project src/Aperture.Web set \"Auth:Username\" \"{username}\"");
        Console.WriteLine($"  dotnet user-secrets --project src/Aperture.Web set \"Auth:PasswordHash\" \"{hash}\"");
        Console.WriteLine();
        Console.WriteLine("Production (environment variables):");
        Console.WriteLine($"  Auth__Username={username}");
        Console.WriteLine($"  Auth__PasswordHash={hash}");
        return 0;
    }

    private static string ReadHidden(string prompt)
    {
        Console.Write(prompt);
        if (Console.IsInputRedirected)
        {
            return Console.ReadLine() ?? "";
        }

        var chars = new List<char>();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                break;
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (chars.Count > 0) chars.RemoveAt(chars.Count - 1);
            }
            else if (!char.IsControl(key.KeyChar))
            {
                chars.Add(key.KeyChar);
            }
        }

        Console.WriteLine();
        return new string(chars.ToArray());
    }
}

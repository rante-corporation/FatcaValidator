using FatcaValidator.Cli;

// Color only an interactive console; NO_COLOR (https://no-color.org) turns it off.
bool useColor = !Console.IsOutputRedirected && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));

return CliApp.Run(args, Console.Out, Console.Error, useColor);

using Mark.LicenceServer;

// MARK Licence Server. Options (command line or appsettings.json):
//   --DataFolder <folder>       where licences.db and signing-key.pem are kept (default %LOCALAPPDATA%\MARK Licence Server)
//   --SigningKeyPath <file>     the private signing key, if kept elsewhere
//   --Urls <urls>               the address(es) to listen on (default http://localhost:5180)
var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddCommandLine(args)
    .Build();

var defaults = new ServerOptions();
var options = new ServerOptions
{
    DataFolder = configuration["DataFolder"] is { Length: > 0 } folder ? folder : defaults.DataFolder,
    SigningKeyPath = configuration["SigningKeyPath"] is { Length: > 0 } key ? key : null,
    Urls = configuration["Urls"] is { Length: > 0 } urls ? urls : defaults.Urls
};

var app = LicenceServerApp.Create(options);
app.Run();

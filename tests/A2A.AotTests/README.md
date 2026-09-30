# Native AOT operation validation

This executable exercises both JSON-RPC and HTTP+JSON endpoint delegates and
clients with source-generated extension request, result, event, and declared
error metadata. It checks successful unary/streaming calls, declared errors
before the first event, and request-scope disposal. Serialization reflection
is disabled. Failed assertions terminate the process with a nonzero exit code.

Run the managed validation:

```powershell
dotnet run --project tests\A2A.AotTests\A2A.AotTests.csproj
```

Publish on Windows from an x64 Visual Studio developer shell:

```powershell
dotnet publish tests\A2A.AotTests\A2A.AotTests.csproj -c Release -r win-x64 -p:PublishAot=true -p:PublishTrimmed=true
```

Run `A2A.AotTests.exe` from the publish directory printed by the command.
The x64 developer shell may insert `x64` into the output path. On other hosts,
use the host's supported Native AOT RID and installed native build tools.

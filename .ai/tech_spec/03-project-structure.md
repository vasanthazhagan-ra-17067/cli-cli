# 3. Project Structure

```
cliq-cli/
├── src/
│   ├── CliqCli/                              ← Entry point + Spectre command wiring
│   │   ├── CliqCli.csproj
│   │   ├── Program.cs
│   │   └── Commands/
│   │       ├── AccountCommands.cs            ← add, list, remove, show, set-default, re-auth
│   │       ├── ScopeCommands.cs              ← add, remove, list
│   │       └── ApiCommands.cs                ← api call
│   │
│   ├── CliqCli.Core/                         ← Domain logic (no CLI concerns)
│   │   ├── CliqCli.Core.csproj
│   │   ├── Auth/
│   │   │   ├── IAuthProvider.cs
│   │   │   └── PatAuthProvider.cs
│   │   ├── Accounts/
│   │   │   ├── AccountStore.cs               ← JSON config read/write
│   │   │   └── AccountConfig.cs              ← AccountEntry model + AccountsRoot DTO
│   │   └── Api/
│   │       └── ApiClient.cs                  ← HttpClient wrapper; injects auth header
│   │
│   └── CliqCli.Keychain/                     ← OS keychain abstraction
│       ├── CliqCli.Keychain.csproj
│       ├── IKeychainProvider.cs
│       ├── MacOsKeychainProvider.cs           ← macOS Security.framework via P/Invoke
│       ├── WindowsKeychainProvider.cs         ← Windows Credential Manager via P/Invoke
│       ├── LinuxKeychainProvider.cs           ← libsecret / Secret Service via P/Invoke
│       └── EncryptedFileKeychainProvider.cs   ← fallback: AES-encrypted file
│
└── tests/
    └── CliqCli.Tests/
        ├── CliqCli.Tests.csproj
        ├── AccountStoreTests.cs
        ├── PatAuthProviderTests.cs
        └── ApiClientTests.cs
```

## Project References

```
CliqCli  →  CliqCli.Core
CliqCli  →  CliqCli.Keychain
CliqCli.Core  →  CliqCli.Keychain
```

namespace Rinha

open System
open Falco
open Falco.Routing
open Falco.HostBuilder
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Npgsql

module Program =
    [<EntryPoint>]
    let main args =
        let env = Environment.GetEnvironmentVariable "ASPNETCORE_ENVIRONMENT"
        let config = configuration [||] {
            required_json "appsettings.json"
            optional_json $"appsettings.{env}.json"
        }
        webHost args {
            add_service (_.AddNpgsqlDataSource(config.GetConnectionString("Default")))

            endpoints
                [ post "/clientes/{id}/transacoes" Controller.transaction
                  get "/clientes/{id}/extrato" Controller.balance ]
        }

        0

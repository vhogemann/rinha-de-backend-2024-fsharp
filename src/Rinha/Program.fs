module Rinha.Program

open System
open System.Data
open System.Text.Json
open Falco
open Falco.Routing
open Falco.HostBuilder
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Npgsql

module Model =
    
    let options = JsonSerializerOptions()
    options.PropertyNamingPolicy <- JsonNamingPolicy.SnakeCaseLower
    
    type TransacaoRequest =
        { valor: int
          tipo: string
          descricao: string }

    type TransacaoResponse = { limite: int; saldo: int }

    type ExtratoResponse =
        { saldo: ExtratoSaldoResponse
          transacoes: ExtratoTransacaoResponse list }

    and ExtratoSaldoResponse =
        { limite: int
          total: int
          dataExtrato: DateTime }

    and ExtratoTransacaoResponse =
        { valor: int
          tipo: string
          descricao: string
          realizadaEm: DateTime }

module Persistence =
    open Donald
    open Model

    let transacaoResposneDataReader (rd: IDataReader) : TransacaoResponse =
         { saldo = rd.ReadInt32 "amount" 
           limite = rd.ReadInt32 "overdraft_limit" }

    let balanceDataReader (rd: IDataReader) : ExtratoSaldoResponse =
        { total = rd.ReadInt32 "amount"
          limite = rd.ReadInt32 "overdraft_limit"
          dataExtrato = DateTime.Now }

    let tipoMapper =
        function
        | "DEPOSIT" -> "c"
        | "WITHDRAWAL" -> "d"
        | _ -> "?"

    let transactionDataReader (rd: IDataReader) : ExtratoTransacaoResponse =
        { valor = rd.ReadInt32 "amount"
          tipo = rd.ReadString "transaction_type" |> tipoMapper
          descricao = rd.ReadString "description"
          realizadaEm = rd.ReadDateTime "transaction_date" }

    let withdrawal (dbconn: NpgsqlConnection) (clientId: int, amount: int, description: string) =
        let sql =
            "CALL withdrawal(@clientId, @amount, @description);
             SELECT amount, overdraft_limit FROM balance WHERE client_id = @clientId;"

        let parameters =
            [ "@clientId", sqlInt32 clientId
              "@amount", sqlInt32 amount
              "@description", sqlString description ]

        dbconn
        |> Db.newCommand sql
        |> Db.setParams parameters
        |> Db.Async.querySingle transacaoResposneDataReader

    let deposit (dbconn: NpgsqlConnection) (clientId: int, amount: int, description: string) =
        let sql =
            "CALL deposit(@clientId, @amount, @description);
             SELECT amount, overdraft_limit FROM balance WHERE client_id = @clientId;"

        let parameters =
            [ "@clientId", sqlInt32 clientId
              "@amount", sqlInt32 amount
              "@description", sqlString description ]

        dbconn
        |> Db.newCommand sql
        |> Db.setParams parameters
        |> Db.Async.querySingle transacaoResposneDataReader

    let getBalance (dbconn: NpgsqlConnection) (clientId: int) =
        let sql = "SELECT amount, overdraft_limit FROM balance WHERE client_id = @clientId"
        let parameters = [ "@clientId", sqlInt32 clientId ]

        dbconn
        |> Db.newCommand sql
        |> Db.setParams parameters
        |> Db.Async.querySingle balanceDataReader

    let getTransactions (dbconn: NpgsqlConnection) (clientId: int) =
        let sql =
            """
            SELECT 
                amount, 
                transaction_type, 
                description, 
                transaction_date 
            FROM 
                transactions 
            WHERE 
                client_id = @clientId
            ORDER BY
                transaction_date DESC
            LIMIT 10
                """

        let parameters = [ "@clientId", sqlInt32 clientId ]

        dbconn
        |> Db.newCommand sql
        |> Db.setParams parameters
        |> Db.Async.query transactionDataReader

module Controller =
    open Model

    let optionToResponse (res: 'a option) =
        match res with
        | Some x -> Response.ofJsonOptions options x
        | None -> Response.withStatusCode 404 >> Response.ofEmpty

    let deserialize ctx = task {
        try
            let! obj = Request.getJsonOptions options ctx
            return Ok obj
        with ex ->
            return Error ex
        }
    
    let balance =
        Services.inject<NpgsqlConnection> (fun dbconn ->
            fun ctx ->
                task {
                    let clientId = (Request.getRoute ctx).GetInt "id" |> int

                    let! mayBeSaldo = Persistence.getBalance dbconn clientId
                    let! transacoes = Persistence.getTransactions dbconn clientId

                    return
                        mayBeSaldo
                        |> Option.map (fun saldo ->
                            { saldo = saldo
                              transacoes = transacoes })
                        |> optionToResponse <| ctx
                })

    let transaction =
        Services.inject<NpgsqlConnection> (fun dbconn ->
            fun ctx ->
                task {
                    let client_id = (Request.getRoute ctx).GetInt "id"
                    let! request = deserialize ctx
                    match request with
                    | Error _ -> return (Response.withStatusCode 400 >> Response.ofPlainText "Bad Request") ctx
                    | Ok request ->
                    let transaction =
                        match request.tipo with
                        | "c" -> Persistence.deposit // Credito
                        | "d" -> Persistence.withdrawal // Debito
                        | _ -> failwith "Invalid transaction type"
                    try 
                        let! response = transaction dbconn (client_id, request.valor, request.descricao)
                        return response |> optionToResponse <| ctx
                    with _ ->
                        return (Response.withStatusCode 422 >> Response.ofEmpty) ctx
                })

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

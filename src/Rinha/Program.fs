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
          saldo: int
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
        { limite = rd.ReadInt32 "overdraft_limit"
          saldo = rd.ReadInt32 "amount" }

    let balanceDataReader (rd: IDataReader) : ExtratoSaldoResponse =
        { limite = rd.ReadInt32 "overdraft_limit"
          saldo = rd.ReadInt32 "amount"
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

    let optionToNotFound (res: 'a option) =
        match res with
        | Some x -> Response.ofJson x
        | None -> Response.withStatusCode 404 >> Response.ofEmpty

    let balance =
        Services.inject<NpgsqlConnection> (fun dbconn ->
            fun ctx ->
                task {
                    let clientId = (Request.getRoute ctx).GetString "id" |> int

                    let! mayBeSaldo = Persistence.getBalance dbconn clientId
                    let! transacoes = Persistence.getTransactions dbconn clientId

                    return
                        mayBeSaldo
                        |> Option.map (fun saldo ->
                            { saldo = saldo
                              transacoes = transacoes })
                })

    let transaction =
        Services.inject<NpgsqlConnection> (fun dbconn ->
            fun ctx ->
                task {
                    let client_id = (Request.getRoute ctx).GetString "id"
                    let! json = Request.getBodyString ctx
                    let request = json |> JsonSerializer.Deserialize<TransacaoRequest>

                    let transaction =
                        match request.tipo with
                        | "c" -> Persistence.deposit // Credito
                        | "d" -> Persistence.withdrawal // Debito
                        | _ -> failwith "Invalid transaction type"

                    let! response = transaction dbconn (int client_id, request.valor, request.descricao)
                    return response |> optionToNotFound
                })

[<EntryPoint>]
let main args =
    let config =
        ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional = false, reloadOnChange = true)
            .AddJsonFile("appsettings.Development.json", optional = true, reloadOnChange = true)
            .Build()
        :> IConfiguration

    webHost args {
        add_service (_.AddNpgsqlDataSource(config.GetConnectionString("DefaultConnection")))

        endpoints
            [ post "/clientes/{id}/transacoes" Controller.transaction
              get "/clientes/{id}/extrato" Controller.balance ]
    }

    0

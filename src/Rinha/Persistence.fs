module Rinha.Persistence

open Donald
open Model
open Npgsql
open System
open System.Data

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
        SELECT amount, transaction_type, description, transaction_date 
        FROM transactions 
        WHERE client_id = @clientId
        ORDER BY transaction_date DESC LIMIT 10
        """
    let parameters = [ "@clientId", sqlInt32 clientId ]
    dbconn
    |> Db.newCommand sql
    |> Db.setParams parameters
    |> Db.Async.query transactionDataReader

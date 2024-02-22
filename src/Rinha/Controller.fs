module Rinha.Controller

open Falco
open Model
open Npgsql

let shouldSkipId id =
    match id with
    | 1 | 2 | 3 | 4 | 5 | 99 -> false
    | _ -> true

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
                if shouldSkipId clientId then
                    return (Response.withStatusCode 404 >> Response.ofEmpty) ctx
                else
                let! mayBeSaldo = Persistence.getBalance dbconn clientId
                let! transacoes = Persistence.getTransactions dbconn clientId
                return
                    mayBeSaldo
                    |> Option.map (fun saldo ->
                        { saldo = saldo
                          ultimasTransacoes = transacoes })
                    |> optionToResponse <| ctx
            })

let transaction =
    Services.inject<NpgsqlConnection> (fun dbconn ->
        fun ctx ->
            task {
                let clientId = (Request.getRoute ctx).GetInt "id"
                if shouldSkipId clientId then
                    return (Response.withStatusCode 404 >> Response.ofEmpty) ctx
                else
                let! request = deserialize ctx
                match request with
                | Error _ -> return (Response.withStatusCode 422 >> Response.ofPlainText "Bad Request") ctx
                | Ok request ->
                if request.valor <= 0 || request.descricao = null || request.descricao.Length > 10 || request.descricao.Length = 0 then
                    return (Response.withStatusCode 422 >> Response.ofPlainText "Bad Request") ctx
                else
                try
                    let! response =
                        match request.tipo with
                        | "c" -> Persistence.deposit dbconn (clientId, request.valor, request.descricao) // Credito
                        | "d" -> Persistence.withdrawal dbconn (clientId, request.valor, request.descricao) // Debito
                        | _ -> failwith "Invalid transaction type"
                    return response |> optionToResponse <| ctx
                with _ ->
                    return (Response.withStatusCode 422 >> Response.ofEmpty) ctx
            })

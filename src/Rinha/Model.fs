module Rinha.Model

open System
open System.Text.Json

let options = JsonSerializerOptions()
options.PropertyNamingPolicy <- JsonNamingPolicy.SnakeCaseLower

type TransacaoRequest =
    { valor: int
      tipo: string
      descricao: string }

type TransacaoResponse = { limite: int; saldo: int }

type ExtratoResponse =
    { saldo: ExtratoSaldoResponse
      ultimasTransacoes: ExtratoTransacaoResponse list }
and ExtratoSaldoResponse =
    { limite: int
      total: int
      dataExtrato: DateTime }
and ExtratoTransacaoResponse =
    { valor: int
      tipo: string
      descricao: string
      realizadaEm: DateTime }
namespace HemodinksAPI.Domain.Models;

public enum AtendimentoCirurgicoStatus { Planejado, Autorizado, Realizado, Cancelado }
public enum FaturamentoStatus { Rascunho, ProntoParaEnvio, Enviado, EmAnalise, GlosadoParcial, GlosadoTotal, Aprovado, ParcialmentePago, Pago, Cancelado }
public enum FaturamentoItemStatus { Rascunho, Apresentado, GlosadoParcial, GlosadoTotal, Aprovado, Cancelado }
public enum GlosaStatus { Aberta, Aceita, EmRecurso, RevertidaParcial, RevertidaTotal }
public enum RecursoGlosaStatus { EmPreparacao, Enviado, Aceito, AceitoParcialmente, Negado, Cancelado }
public enum ContaReceberStatus { Previsto, Aberto, ParcialmenteRecebido, Recebido, Vencido, Cancelado }
public enum FormaRecebimento { Pix, Transferencia, Boleto, Dinheiro, Cartao, Deposito, Outro }


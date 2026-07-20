using DuneFlame.Application.DTOs.Basket;
using DuneFlame.Application.DTOs.Payment;
using DuneFlame.Application.Interfaces;
using MediatR;

namespace DuneFlame.Application.Payments.Commands.CreatePaymentIntent;

public class CreatePaymentIntentCommandHandler(
    IBasketService basketService,
    IPaymentService paymentService,
    IOrderService orderService)
    : IRequestHandler<CreatePaymentIntentCommand, PaymentIntentDto>
{
    public async Task<PaymentIntentDto> Handle(CreatePaymentIntentCommand command, CancellationToken cancellationToken)
    {
        var basket = await basketService.GetBasketAsync(command.BasketId);
        if (basket == null || basket.Items.Count == 0)
            throw new InvalidOperationException("Basket is empty or not found.");

        // Internal (zero-payment) basket — return immediately without calling Stripe
        if (!string.IsNullOrEmpty(basket.PaymentIntentId) && basket.PaymentIntentId.StartsWith("internal_"))
        {
            return new PaymentIntentDto(
                ClientSecret: string.Empty,
                PaymentIntentId: basket.PaymentIntentId,
                Amount: 0,
                PaymentNotRequired: true);
        }

        decimal totalAmount = basket.Items.Sum(item => item.Price * item.Quantity);

        // Welcome discount (10%) for first-time buyers
        if (command.UserId.HasValue)
        {
            bool hasOrders = await orderService.HasCompletedOrdersAsync(command.UserId.Value);
            if (!hasOrders)
                totalAmount -= Math.Round(totalAmount * 0.10m, 2);
        }

        if (totalAmount <= 0)
            throw new InvalidOperationException("Invalid basket total.");

        var paymentIntent = await paymentService.CreateOrUpdatePaymentIntentAsync(
            command.BasketId,
            totalAmount,
            basket.CurrencyCode.ToString().ToLower());

        // Sync PaymentIntentId onto the latest pending order (authenticated users only)
        if (command.UserId.HasValue && !paymentIntent.PaymentIntentId.StartsWith("internal_"))
            await orderService.SyncPaymentIntentIdAsync(command.UserId.Value, paymentIntent.PaymentIntentId);

        bool paymentNotRequired = string.IsNullOrEmpty(paymentIntent.ClientSecret) &&
                                  paymentIntent.PaymentIntentId.StartsWith("internal_");

        return new PaymentIntentDto(
            paymentIntent.ClientSecret,
            paymentIntent.PaymentIntentId,
            paymentIntent.Amount,
            paymentNotRequired);
    }
}

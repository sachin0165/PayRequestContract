using Stratis.SmartContracts;
using System;

[Deploy]
public class PaymentRequest : SmartContract
{
    public PaymentRequest(ISmartContractState smartContractState, ulong totalSupply, string name, string symbol, ulong serviceFee)
    : base(smartContractState)
    {
        Owner = Message.Sender;
        TotalSupply = totalSupply;
        Name = name;
        Symbol = symbol;
        SetDecimals(8);
        ServiceFee = serviceFee;

        this.SetBalance(Message.Sender, this.TotalSupply);
    }

    #region Standard token

    public string Symbol
    {
        get => State.GetString(nameof(this.Symbol));
        private set => State.SetString(nameof(this.Symbol), value);
    }
    public string Name
    {
        get => State.GetString(nameof(this.Name));
        private set => State.SetString(nameof(this.Name), value);
    }
    public ulong TotalSupply
    {
        get => State.GetUInt64(nameof(this.TotalSupply));
        private set => State.SetUInt64(nameof(this.TotalSupply), value);
    }
    public ulong GetBalance(Address address)
    {
        return State.GetUInt64($"Balance:{address}");
    }
    private void SetBalance(Address address, ulong value)
    {
        State.SetUInt64($"Balance:{address}", value);
    }
    public uint GetDecimals()
    {
        return State.GetUInt32("Decimals");
    }
    private void SetDecimals(uint value)
    {
        State.SetUInt32("Decimals", value);
    }
    public bool TransferTo(Address to, ulong amount)
    {
        if (amount == 0)
        {
            Log(new TransferLog { From = Message.Sender, To = to, Amount = 0 });

            return true;
        }

        ulong senderBalance = GetBalance(Message.Sender);

        if (senderBalance < amount)
        {
            return false;
        }

        SetBalance(Message.Sender, senderBalance - amount);

        SetBalance(to, checked(GetBalance(to) + amount));

        Log(new TransferLog { From = Message.Sender, To = to, Amount = amount });

        return true;
    }
    public bool TransferFrom(Address from, Address to, ulong amount)
    {
        if (amount == 0)
        {
            Log(new TransferLog { From = from, To = to, Amount = 0 });

            return true;
        }

        ulong senderAllowance = Allowance(from, Message.Sender);
        ulong fromBalance = GetBalance(from);

        if (senderAllowance < amount || fromBalance < amount)
        {
            return false;
        }

        SetApproval(from, Message.Sender, senderAllowance - amount);

        SetBalance(from, fromBalance - amount);

        SetBalance(to, checked(GetBalance(to) + amount));

        Log(new TransferLog { From = from, To = to, Amount = amount });

        return true;
    }
    public bool Approve(Address spender, ulong currentAmount, ulong amount)
    {
        if (Allowance(Message.Sender, spender) != currentAmount)
        {
            return false;
        }

        SetApproval(Message.Sender, spender, amount);

        Log(new ApprovalLog { Owner = Message.Sender, Spender = spender, Amount = amount, OldAmount = currentAmount });

        return true;
    }
    private void SetApproval(Address owner, Address spender, ulong value)
    {
        State.SetUInt64($"Allowance:{owner}:{spender}", value);
    }
    public ulong Allowance(Address owner, Address spender)
    {
        return State.GetUInt64($"Allowance:{owner}:{spender}");
    }
    public struct TransferLog
    {
        [Index]
        public Address From;

        [Index]
        public Address To;

        public ulong Amount;
    }
    public struct ApprovalLog
    {
        [Index]
        public Address Owner;

        [Index]
        public Address Spender;

        public ulong OldAmount;

        public ulong Amount;
    }

    #endregion

    #region Payment Request
    public Address Owner
    {
        get => State.GetAddress(nameof(Owner));
        private set => State.SetAddress(nameof(Owner), value);
    }
    public ulong ServiceFee
    {
        get => State.GetUInt64(nameof(ServiceFee));
        private set => State.SetUInt64(nameof(ServiceFee), value);
    }
    private void SetPaymentRequest(uint id, Request paymentRequest) => State.SetStruct($"PaymentRequest:{id}", paymentRequest);
    public Request GetPaymentRequest(uint id) => State.GetStruct<Request>($"PaymentRequest:{id}");
    private void EnsureOwnerOnly() => Assert(this.Owner == Message.Sender, "The method is owner only.");
    public void ReviseServiceFee(ulong newServiceFee)
    {
        EnsureOwnerOnly();

        var oldServiceFee = ServiceFee;
        ServiceFee = newServiceFee;

        Log(new ServiceFeeLog
        {
            Owner = Owner,
            OldFee = oldServiceFee,
            NewFee = newServiceFee,
        });
    }
    public bool CreateRequest(uint id, string description, Address recipientAddress, ulong amount, ulong expiry)
    {
        Assert(GetPaymentRequest(id).Id == 0, "Payment request is already present with given Id.");

        Assert(Message.Sender != recipientAddress, "Recipient address should be different.");

        var request = new Request()
        {
            Id = id,
            CreatorAddress = Message.Sender,
            RecipientAddress = recipientAddress,
            Description = description,
            Amount = amount,
            Expiry = expiry,
            Status = (int)PaymentStatus.Created
        };

        SetPaymentRequest(id, request);

        var transferResult = TransferTo(Owner, ServiceFee);
        Assert(transferResult, "Fee transfer failed.");

        Log(request);

        return true;
    }
    public bool PayRequest(uint id, ulong currentTime)
    {
        var paymentRequest = GetPaymentRequest(id);

        Assert(paymentRequest.Id > 0, "Payment request is not present.");

        Assert(paymentRequest.RecipientAddress == Message.Sender, "Invalid payer.");

        Assert(paymentRequest.Expiry > currentTime, "The request is expired.");

        Assert(paymentRequest.Status == (int)PaymentStatus.Created, "The request is paid or canceled.");

        var transferResult = TransferTo(paymentRequest.CreatorAddress, paymentRequest.Amount);

        Assert(transferResult, "Payment failed.");

        paymentRequest.Status = (int)PaymentStatus.Paid;

        SetPaymentRequest(paymentRequest.Id, paymentRequest);

        Log(paymentRequest);

        return true;
    }
    public bool CancelRequest(uint id)
    {
        var paymentRequest = GetPaymentRequest(id);

        Assert(paymentRequest.Id > 0, "Payment request is not present.");

        Assert(paymentRequest.Status == (int)PaymentStatus.Created, "Only created request can cancel.");

        Assert(paymentRequest.CreatorAddress == Message.Sender, "Only request creator can cancel.");

        paymentRequest.Status = (int)PaymentStatus.Cancelled;

        SetPaymentRequest(paymentRequest.Id, paymentRequest);

        Log(paymentRequest);

        return true;
    }

    public struct Request
    {
        [Index]
        public uint Id;

        public Address CreatorAddress;

        public Address RecipientAddress;

        public ulong Amount;

        public string Description;

        public int Status;

        public ulong Expiry;
    }

    public struct ServiceFeeLog
    {
        [Index]
        public Address Owner;

        public ulong OldFee;

        public ulong NewFee;
    }

    public enum PaymentStatus : int
    {
        Created,
        Cancelled,
        Paid
    }

    #endregion
}
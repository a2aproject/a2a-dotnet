// Auto-generated from a2a-itk/protos/instruction.proto
// Manually translated to C# using Google.Protobuf runtime.

using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace A2A.Itk.Proto;

/// <summary>Root instruction message dispatched through the agent chain.</summary>
public sealed class Instruction : IMessage<Instruction>
{
    public enum StepOneofCase
    {
        None = 0,
        CallAgent = 1,
        ReturnResponse = 2,
        Steps = 3,
    }

    private StepOneofCase _stepCase = StepOneofCase.None;
    private object? _step;

    public StepOneofCase StepCase => _stepCase;

    public CallAgent? CallAgent
    {
        get => _stepCase == StepOneofCase.CallAgent ? (CallAgent?)_step : null;
        set { _step = value; _stepCase = value is null ? StepOneofCase.None : StepOneofCase.CallAgent; }
    }

    public ReturnResponse? ReturnResponse
    {
        get => _stepCase == StepOneofCase.ReturnResponse ? (ReturnResponse?)_step : null;
        set { _step = value; _stepCase = value is null ? StepOneofCase.None : StepOneofCase.ReturnResponse; }
    }

    public SeriesOfSteps? Steps
    {
        get => _stepCase == StepOneofCase.Steps ? (SeriesOfSteps?)_step : null;
        set { _step = value; _stepCase = value is null ? StepOneofCase.None : StepOneofCase.Steps; }
    }

    public MessageDescriptor Descriptor => null!;
    public int CalculateSize()
    {
        int size = 0;
        switch (_stepCase)
        {
            case StepOneofCase.CallAgent:
                size += 1 + CodedOutputStream.ComputeMessageSize((CallAgent)_step!);
                break;
            case StepOneofCase.ReturnResponse:
                size += 1 + CodedOutputStream.ComputeMessageSize((ReturnResponse)_step!);
                break;
            case StepOneofCase.Steps:
                size += 1 + CodedOutputStream.ComputeMessageSize((SeriesOfSteps)_step!);
                break;
        }
        return size;
    }

    public Instruction Clone() => new()
    {
        _stepCase = _stepCase,
        _step = _stepCase switch
        {
            StepOneofCase.CallAgent => ((CallAgent)_step!).Clone(),
            StepOneofCase.ReturnResponse => ((ReturnResponse)_step!).Clone(),
            StepOneofCase.Steps => ((SeriesOfSteps)_step!).Clone(),
            _ => null,
        }
    };

    public bool Equals(Instruction? other) => other is not null && _stepCase == other._stepCase;

    public void MergeFrom(Instruction message)
    {
        switch (message._stepCase)
        {
            case StepOneofCase.CallAgent:
                CallAgent = message.CallAgent!.Clone();
                break;
            case StepOneofCase.ReturnResponse:
                ReturnResponse = message.ReturnResponse!.Clone();
                break;
            case StepOneofCase.Steps:
                Steps = message.Steps!.Clone();
                break;
        }
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            switch (tag)
            {
                case 10: // field 1, wire type LEN
                    var ca = new CallAgent();
                    input.ReadMessage(ca);
                    CallAgent = ca;
                    break;
                case 18: // field 2, wire type LEN
                    var rr = new ReturnResponse();
                    input.ReadMessage(rr);
                    ReturnResponse = rr;
                    break;
                case 26: // field 3, wire type LEN
                    var ss = new SeriesOfSteps();
                    input.ReadMessage(ss);
                    Steps = ss;
                    break;
                default:
                    input.SkipLastField();
                    break;
            }
        }
    }

    public void WriteTo(CodedOutputStream output)
    {
        switch (_stepCase)
        {
            case StepOneofCase.CallAgent:
                output.WriteTag(1, WireFormat.WireType.LengthDelimited);
                output.WriteMessage((CallAgent)_step!);
                break;
            case StepOneofCase.ReturnResponse:
                output.WriteTag(2, WireFormat.WireType.LengthDelimited);
                output.WriteMessage((ReturnResponse)_step!);
                break;
            case StepOneofCase.Steps:
                output.WriteTag(3, WireFormat.WireType.LengthDelimited);
                output.WriteMessage((SeriesOfSteps)_step!);
                break;
        }
    }
}

public sealed class CallAgent : IMessage<CallAgent>
{
    public enum BehaviorOneofCase
    {
        None = 0,
        SendMessage = 5,
        PushNotification = 6,
        Resubscribe = 7,
    }

    public string Transport { get; set; } = "";
    public string AgentCardUri { get; set; } = "";
    public Instruction? Instruction { get; set; }
    public bool Streaming { get; set; }

    private BehaviorOneofCase _behaviorCase = BehaviorOneofCase.None;
    private object? _behavior;

    public BehaviorOneofCase BehaviorCase => _behaviorCase;

    public SendMessageBehavior? SendMessage
    {
        get => _behaviorCase == BehaviorOneofCase.SendMessage ? (SendMessageBehavior?)_behavior : null;
        set { _behavior = value; _behaviorCase = value is null ? BehaviorOneofCase.None : BehaviorOneofCase.SendMessage; }
    }

    public PushNotificationBehavior? PushNotification
    {
        get => _behaviorCase == BehaviorOneofCase.PushNotification ? (PushNotificationBehavior?)_behavior : null;
        set { _behavior = value; _behaviorCase = value is null ? BehaviorOneofCase.None : BehaviorOneofCase.PushNotification; }
    }

    public ResubscribeBehavior? Resubscribe
    {
        get => _behaviorCase == BehaviorOneofCase.Resubscribe ? (ResubscribeBehavior?)_behavior : null;
        set { _behavior = value; _behaviorCase = value is null ? BehaviorOneofCase.None : BehaviorOneofCase.Resubscribe; }
    }

    public MessageDescriptor Descriptor => null!;

    public int CalculateSize()
    {
        int size = 0;
        if (Transport.Length > 0) size += 1 + CodedOutputStream.ComputeStringSize(Transport);
        if (AgentCardUri.Length > 0) size += 1 + CodedOutputStream.ComputeStringSize(AgentCardUri);
        if (Instruction is not null) size += 1 + CodedOutputStream.ComputeMessageSize(Instruction);
        if (Streaming) size += 1 + 1;
        switch (_behaviorCase)
        {
            case BehaviorOneofCase.SendMessage:
                size += 1 + CodedOutputStream.ComputeMessageSize((SendMessageBehavior)_behavior!);
                break;
            case BehaviorOneofCase.PushNotification:
                size += 1 + CodedOutputStream.ComputeMessageSize((PushNotificationBehavior)_behavior!);
                break;
            case BehaviorOneofCase.Resubscribe:
                size += 1 + CodedOutputStream.ComputeMessageSize((ResubscribeBehavior)_behavior!);
                break;
        }
        return size;
    }

    public CallAgent Clone() => new()
    {
        Transport = Transport,
        AgentCardUri = AgentCardUri,
        Instruction = Instruction?.Clone(),
        Streaming = Streaming,
        _behaviorCase = _behaviorCase,
        _behavior = _behaviorCase switch
        {
            BehaviorOneofCase.SendMessage => ((SendMessageBehavior)_behavior!).Clone(),
            BehaviorOneofCase.PushNotification => ((PushNotificationBehavior)_behavior!).Clone(),
            BehaviorOneofCase.Resubscribe => ((ResubscribeBehavior)_behavior!).Clone(),
            _ => null,
        }
    };

    public bool Equals(CallAgent? other) => other is not null;

    public void MergeFrom(CallAgent message)
    {
        if (message.Transport.Length > 0) Transport = message.Transport;
        if (message.AgentCardUri.Length > 0) AgentCardUri = message.AgentCardUri;
        if (message.Instruction is not null) Instruction = message.Instruction.Clone();
        if (message.Streaming) Streaming = message.Streaming;
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            switch (tag)
            {
                case 10: Transport = input.ReadString(); break;
                case 18: AgentCardUri = input.ReadString(); break;
                case 26:
                    Instruction ??= new Instruction();
                    input.ReadMessage(Instruction);
                    break;
                case 32: Streaming = input.ReadBool(); break;
                case 42:
                    var sm = new SendMessageBehavior();
                    input.ReadMessage(sm);
                    SendMessage = sm;
                    break;
                case 50:
                    var pn = new PushNotificationBehavior();
                    input.ReadMessage(pn);
                    PushNotification = pn;
                    break;
                case 58:
                    var rs = new ResubscribeBehavior();
                    input.ReadMessage(rs);
                    Resubscribe = rs;
                    break;
                default:
                    input.SkipLastField();
                    break;
            }
        }
    }

    public void WriteTo(CodedOutputStream output)
    {
        if (Transport.Length > 0) { output.WriteTag(1, WireFormat.WireType.LengthDelimited); output.WriteString(Transport); }
        if (AgentCardUri.Length > 0) { output.WriteTag(2, WireFormat.WireType.LengthDelimited); output.WriteString(AgentCardUri); }
        if (Instruction is not null) { output.WriteTag(3, WireFormat.WireType.LengthDelimited); output.WriteMessage(Instruction); }
        if (Streaming) { output.WriteTag(4, WireFormat.WireType.Varint); output.WriteBool(Streaming); }
        switch (_behaviorCase)
        {
            case BehaviorOneofCase.SendMessage:
                output.WriteTag(5, WireFormat.WireType.LengthDelimited); output.WriteMessage((SendMessageBehavior)_behavior!);
                break;
            case BehaviorOneofCase.PushNotification:
                output.WriteTag(6, WireFormat.WireType.LengthDelimited); output.WriteMessage((PushNotificationBehavior)_behavior!);
                break;
            case BehaviorOneofCase.Resubscribe:
                output.WriteTag(7, WireFormat.WireType.LengthDelimited); output.WriteMessage((ResubscribeBehavior)_behavior!);
                break;
        }
    }
}

public sealed class ReturnResponse : IMessage<ReturnResponse>
{
    public string Response { get; set; } = "";
    public bool HoldTask { get; set; }

    public MessageDescriptor Descriptor => null!;
    public int CalculateSize()
    {
        int size = 0;
        if (Response.Length > 0) size += 1 + CodedOutputStream.ComputeStringSize(Response);
        if (HoldTask) size += 1 + 1;
        return size;
    }
    public ReturnResponse Clone() => new() { Response = Response, HoldTask = HoldTask };
    public bool Equals(ReturnResponse? other) => other is not null && Response == other.Response;
    public void MergeFrom(ReturnResponse message) { if (message.Response.Length > 0) Response = message.Response; if (message.HoldTask) HoldTask = message.HoldTask; }
    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            switch (tag)
            {
                case 10: Response = input.ReadString(); break;
                case 16: HoldTask = input.ReadBool(); break;
                default: input.SkipLastField(); break;
            }
        }
    }
    public void WriteTo(CodedOutputStream output)
    {
        if (Response.Length > 0) { output.WriteTag(1, WireFormat.WireType.LengthDelimited); output.WriteString(Response); }
        if (HoldTask) { output.WriteTag(2, WireFormat.WireType.Varint); output.WriteBool(HoldTask); }
    }
}

public sealed class SeriesOfSteps : IMessage<SeriesOfSteps>
{
    public enum ResponseGeneratorType
    {
        Unspecified = 0,
        Concat = 1,
    }

    public List<Instruction> Instructions { get; set; } = [];
    public ResponseGeneratorType ResponseGenerator { get; set; } = ResponseGeneratorType.Unspecified;

    public MessageDescriptor Descriptor => null!;
    public int CalculateSize()
    {
        int size = 0;
        foreach (var inst in Instructions)
            size += 1 + CodedOutputStream.ComputeMessageSize(inst);
        if (ResponseGenerator != ResponseGeneratorType.Unspecified)
            size += 1 + CodedOutputStream.ComputeEnumSize((int)ResponseGenerator);
        return size;
    }
    public SeriesOfSteps Clone() => new()
    {
        Instructions = Instructions.Select(i => i.Clone()).ToList(),
        ResponseGenerator = ResponseGenerator,
    };
    public bool Equals(SeriesOfSteps? other) => other is not null;
    public void MergeFrom(SeriesOfSteps message) { Instructions.AddRange(message.Instructions.Select(i => i.Clone())); }
    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            switch (tag)
            {
                case 10:
                    var inst = new Instruction();
                    input.ReadMessage(inst);
                    Instructions.Add(inst);
                    break;
                case 16:
                    ResponseGenerator = (ResponseGeneratorType)input.ReadEnum();
                    break;
                default:
                    input.SkipLastField();
                    break;
            }
        }
    }
    public void WriteTo(CodedOutputStream output)
    {
        foreach (var inst in Instructions)
        {
            output.WriteTag(1, WireFormat.WireType.LengthDelimited);
            output.WriteMessage(inst);
        }
        if (ResponseGenerator != ResponseGeneratorType.Unspecified)
        {
            output.WriteTag(2, WireFormat.WireType.Varint);
            output.WriteEnum((int)ResponseGenerator);
        }
    }
}

public sealed class SendMessageBehavior : IMessage<SendMessageBehavior>
{
    public MessageDescriptor Descriptor => null!;
    public int CalculateSize() => 0;
    public SendMessageBehavior Clone() => new();
    public bool Equals(SendMessageBehavior? other) => other is not null;
    public void MergeFrom(SendMessageBehavior message) { }
    public void MergeFrom(CodedInputStream input) { while (input.ReadTag() != 0) input.SkipLastField(); }
    public void WriteTo(CodedOutputStream output) { }
}

public sealed class PushNotificationBehavior : IMessage<PushNotificationBehavior>
{
    public string Url { get; set; } = "";

    public MessageDescriptor Descriptor => null!;
    public int CalculateSize() => Url.Length > 0 ? 1 + CodedOutputStream.ComputeStringSize(Url) : 0;
    public PushNotificationBehavior Clone() => new() { Url = Url };
    public bool Equals(PushNotificationBehavior? other) => other is not null && Url == other.Url;
    public void MergeFrom(PushNotificationBehavior message) { if (message.Url.Length > 0) Url = message.Url; }
    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            switch (tag)
            {
                case 10: Url = input.ReadString(); break;
                default: input.SkipLastField(); break;
            }
        }
    }
    public void WriteTo(CodedOutputStream output)
    {
        if (Url.Length > 0) { output.WriteTag(1, WireFormat.WireType.LengthDelimited); output.WriteString(Url); }
    }
}

public sealed class ResubscribeBehavior : IMessage<ResubscribeBehavior>
{
    public MessageDescriptor Descriptor => null!;
    public int CalculateSize() => 0;
    public ResubscribeBehavior Clone() => new();
    public bool Equals(ResubscribeBehavior? other) => other is not null;
    public void MergeFrom(ResubscribeBehavior message) { }
    public void MergeFrom(CodedInputStream input) { while (input.ReadTag() != 0) input.SkipLastField(); }
    public void WriteTo(CodedOutputStream output) { }
}

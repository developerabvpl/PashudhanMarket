namespace UPBazaar.SharedKernel.Messaging;

/// <summary>A request that changes state. Dispatched to exactly one handler.</summary>
public interface ICommand;

/// <summary>A request that changes state and returns a value.</summary>
/// <typeparam name="TResponse">Value produced on success.</typeparam>
public interface ICommand<TResponse>;

/// <summary>A read-only request.</summary>
/// <typeparam name="TResponse">Value produced on success.</typeparam>
public interface IQuery<TResponse>;

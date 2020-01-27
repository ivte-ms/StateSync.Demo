using System;

// Temporary placeholders for the interfaces

namespace Microsoft.MixedReality.Sharing.StateSync
{
    // TODO: replace with the actual interface from StateSync
    public interface Connection
    {
        void Send(ReadOnlySpan<byte> bytes);

        string ConnectionString { get; }
    }

    public delegate void OnMessage(Connection sender, ReadOnlySpan<byte> data);

    // TODO: replace with the actual interface from StateSync
    public interface INetwork
    {
        string ConnectionString { get; }

        Connection GetConnection(string connectionString);

        // Polls a single message from the queue of incoming messages and passes it to onMessage.
        // returns true on success, or false if there were no messages in the queue.
        bool PollMessage(OnMessage onMessage);

        // TODO: add blocking calls
    }
}
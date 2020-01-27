using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class VisualNetworkNode : MonoBehaviour, Microsoft.MixedReality.Sharing.StateSync.INetwork
{
    public string connectionString;
    public string ConnectionString => connectionString;

    // The total travel time will be the sum of delaySeconds values of the two nodes,
    // unless the node sends the message to itself.
    public float delaySeconds = 0.05f;

    Dictionary<string, VisualNetworkEmulator.Connection> connections
        = new Dictionary<string, VisualNetworkEmulator.Connection>();

    Queue<VisualNetworkEmulator.Message> queuedMessages = new Queue<VisualNetworkEmulator.Message>();

    private GameObject connectionPointObject;
    public Renderer Renderer { get; private set; }

    public void UpdateConnectionPointObject(Vector3 graphCenter)
    {
        Bounds bounds = Renderer.bounds;
        bounds.Expand(1.0f);
        connectionPointObject.transform.position = bounds.ClosestPoint(graphCenter);
    }

    public VisualNetworkEmulator.Connection GetConnectionImpl(string connectionString)
    {
        VisualNetworkEmulator.Connection endpoint;
        if (!connections.TryGetValue(connectionString, out endpoint))
        {
            endpoint = new VisualNetworkEmulator.Connection(connectionString, this);
            connections.Add(connectionString, endpoint);
        }
        return endpoint;
    }

    public Microsoft.MixedReality.Sharing.StateSync.Connection GetConnection(string connectionString)
    {
        return GetConnectionImpl(connectionString);
    }

    public bool PollMessage(Microsoft.MixedReality.Sharing.StateSync.OnMessage onMessage)
    {
        if (queuedMessages.Count == 0)
            return false;
        var message = queuedMessages.Dequeue();
        onMessage(message.connection.ReverseConnection, message.data);
        return true;
    }

    public GameObject CreateMessageGameObject()
    {
        return Instantiate(VisualNetworkEmulator.Instance.messagePrefab);
    }

    public void EnqueueDeliveredMessage(VisualNetworkEmulator.Message message)
    {
        queuedMessages.Enqueue(message);
    }

    void Start()
    {
        VisualNetworkEmulator.Instance.networkNodes.Add(ConnectionString, this);
        Renderer = GetComponent<Renderer>();
        connectionPointObject = Instantiate(VisualNetworkEmulator.Instance.endpointPrefab);
    }

    void Update()
    {

    }
}

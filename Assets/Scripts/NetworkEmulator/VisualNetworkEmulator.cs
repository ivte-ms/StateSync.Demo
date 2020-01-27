using System;
using System.Collections.Generic;
using System.Threading;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

public class VisualNetworkEmulator : MonoBehaviour
{
    public UnityEngine.UI.Text timeDilationText;
    public GameObject messagePrefab;
    public GameObject endpointPrefab;
    public int minTimeDilation = 1;
    public int maxTimeDilation = 100;

    private int timeDilation = 1;

    internal Dictionary<string, VisualNetworkNode> networkNodes = new Dictionary<string, VisualNetworkNode>();
    private float dilatedTimeNow = 0;

    List<Message> messages = new List<Message>();

    public class Message
    {
        public readonly Connection connection;
        public readonly byte[] data;

        public readonly float arrivalDilatedTimeSeconds;

        private GameObject messageGameObject;

        public Message(Connection connection, byte[] data)
        {
            this.connection = connection;
            this.data = data;
            arrivalDilatedTimeSeconds = instance.dilatedTimeNow + connection.DelaySeconds;
        }

        // Returns true if the message should be removed from the list of messages since it was successfully delivered.
        public bool TryToDeliver(float dilatedTimeNow)
        {
            if (arrivalDilatedTimeSeconds <= dilatedTimeNow)
            {
                if (messageGameObject != null)
                    Destroy(messageGameObject);
                connection.DstNode.EnqueueDeliveredMessage(this);
                return true;
            }
            // In (0..1] interval
            float progressLeft = (arrivalDilatedTimeSeconds - dilatedTimeNow) * connection.InverseDelaySeconds;
            Vector3 position = Vector3.Lerp(
                connection.SrcNode.transform.position,
                connection.DstNode.transform.position,
                progressLeft);
            if (messageGameObject == null)
                messageGameObject = connection.SrcNode.CreateMessageGameObject();
            messageGameObject.transform.position = position;
            return false;  // Not ready to be delivered
        }
    }

    public class Connection : Microsoft.MixedReality.Sharing.StateSync.Connection
    {
        public string ConnectionString { get; private set; }
        public VisualNetworkNode SrcNode { get; private set; }
        public VisualNetworkNode DstNode { get; private set; }

        // Cached just for convenience
        public Connection ReverseConnection { get; private set; }

        //private VisualNetworkNode destinationNode;

        // Initialized after the construction (endpoints are created in pairs by the emulator,
        // so to completely initialize both pairs we need both objects).
        //public Connection Sender { get; private set; }

        public float DelaySeconds { get; private set; } = 0;
        public float InverseDelaySeconds { get; private set; } = 0;  // Irrelevant in case of loop back
        private LineRenderer lineRenderer;  // TODO

        public Connection(string connectionString, VisualNetworkNode srcNode)
        {
            ConnectionString = connectionString;
            SrcNode = srcNode;
        }

        public void Send(ReadOnlySpan<byte> bytes)
        {
            if (DstNode == null)
            {
                VisualNetworkNode dstNode;
                if (!instance.networkNodes.TryGetValue(ConnectionString, out dstNode))
                {
                    // The message won't be delivered
                    return;
                }
                DstNode = dstNode;
                if (SrcNode == dstNode)
                {
                    ReverseConnection = this;
                }
                else
                {
                    DelaySeconds = SrcNode.delaySeconds + dstNode.delaySeconds;
                    InverseDelaySeconds = 1.0f / DelaySeconds;
                    ReverseConnection = dstNode.GetConnectionImpl(SrcNode.ConnectionString);
                }
            }
            instance.messages.Add(new Message(this, bytes.ToArray()));
        }
    }

    public void OnTimeDilationSliderValueChanged(float value)
    {
        float arg = value * value;  // Quadratic to get more control over low values.
        timeDilation = Mathf.RoundToInt(Mathf.Lerp(minTimeDilation, maxTimeDilation, arg));
        timeDilationText.text = "Time dilation: x" + timeDilation.ToString();
    }

    void UpdateNodesPositions()
    {
        int nodesCount = networkNodes.Count;
        if (nodesCount != 0)
        {
            Vector3 graphCenter = new Vector3();
            foreach (var networkNode in networkNodes.Values)
                graphCenter += networkNode.Renderer.bounds.center;
            graphCenter /= nodesCount;
            foreach (var networkNode in networkNodes.Values)
                networkNode.UpdateConnectionPointObject(graphCenter);
        }
    }

    void Update()
    {
        // No real reason to update this each frame, but doing this just for simplicity.
        UpdateNodesPositions();
        messages.RemoveAll(m => m.TryToDeliver(dilatedTimeNow));
    }

    //     public void RegisterNode(VisualNetworkNode node)
    //     {
    //         networkNodes.Add()
    //     }

    private static VisualNetworkEmulator instance;
    public static VisualNetworkEmulator Instance
    {
        get
        {
            if (!instance)
                instance = FindObjectOfType<VisualNetworkEmulator>();
            return instance;
        }
    }
}

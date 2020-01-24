using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEngine;

// TODO: replace with the actual interface from StateSync
public interface IRemoteEndpoint
{
    void Send(ReadOnlySpan<byte> bytes);

    string ConnectionString { get; }
}

public delegate void OnMessage(IRemoteEndpoint sender, ReadOnlySpan<byte> data);

// TODO: replace with the actual interface from StateSync
public interface INetwork
{
    // Returns the connection string that other endpoints can use to get a IRemoteEndpoint pointing to this endpoint.
    string ConnectionString { get; }

    IRemoteEndpoint GetEndpoint(string connectionString);

    // Polls a single message from the queue of incoming messages and passes it to onMessage.
    // returns true on success, or false if there were no messages in the queue.
    bool PollMessage(OnMessage onMessage);

    // TODO: add blocking calls
}

public class NetworkEmulator : MonoBehaviour
{
    public UnityEngine.UI.Text timeDilationText;
    public int minTimeDilation = 1;
    public int maxTimeDilation = 100;

    private int timeDilation = 1;

    private Dictionary<string, NetworkNode> networkNodes = new Dictionary<string, NetworkNode>();
    private float dilatedTimeNow = 0;

    private GameObject messagePrefab;
    private GameObject endpointPrefab;
    List<Message> messages = new List<Message>();

    public class Message
    {
        public readonly RemoteEndpoint dstEndpoint;
        public readonly byte[] data;

        public readonly float arrivalDilatedTime;
        private readonly float inversedTimeDistance;

        private GameObject messageGameObject;

        public Message(RemoteEndpoint dstEndpoint, byte[] data, float arrivalDilatedTime, float inversedTimeDistance)
        {
            this.dstEndpoint = dstEndpoint;
            this.data = data;

            this.arrivalDilatedTime = arrivalDilatedTime;
            this.inversedTimeDistance = inversedTimeDistance;
        }

        // Returns true if the message should be removed from the list of messages since it was successfully delivered.
        public bool TryToDeliver(float dilatedTimeNow)
        {
            if (arrivalDilatedTime <= dilatedTimeNow)
            {
                if (messageGameObject != null)
                    Destroy(messageGameObject);
                dstEndpoint.destinationNode.EnqueueDeliveredMessage(this);
                return true;
            }
            NetworkNode dstNode = dstEndpoint.destinationNode;
            NetworkNode srcNode = dstEndpoint.Sender.destinationNode;
            // In (0..1] interval
            float progressLeft = (arrivalDilatedTime - dilatedTimeNow) * inversedTimeDistance;
            Vector3 position;
            if (NetworkNode.InterpolatePosition(srcNode, dstNode, progressLeft, out position))
            {
                if (messageGameObject == null)
                    messageGameObject = dstEndpoint.Sender.destinationNode.CreateMessageGameObject();
                messageGameObject.transform.position = position;
            }
            return false;  // Not ready to be delivered
        }
    }

    public class RemoteEndpoint : IRemoteEndpoint
    {
        public string ConnectionString { get; }
        public readonly NetworkNode destinationNode;

        // Initialized after the construction (endpoints are created in pairs by the emulator,
        // so to completely initialize both pairs we need both objects).
        public RemoteEndpoint Sender { get; private set; }

        // The amount of dilated seconds (real time seconds scaled by the time dilation slider)
        // that have to pass before the message is delivered.
        readonly float dilatedTimeDistance;
        readonly float inverseDilatedTimeDistance;
        LineRenderer lineRenderer;  // TODO

        public RemoteEndpoint(string connectionString, NetworkNode destinationNode, float dilatedTimeDistance)
        {
            ConnectionString = connectionString;
            this.destinationNode = destinationNode;
            this.dilatedTimeDistance = dilatedTimeDistance;
            inverseDilatedTimeDistance = 1.0f / dilatedTimeDistance;
        }

        public void InitSender(RemoteEndpoint sender)
        {
            if (Sender != null)
                throw new Exception("The endpoint is already initialized");
            Sender = sender;
        }

        public void Send(ReadOnlySpan<byte> bytes)
        {
            if (Sender == null)
                throw new Exception("The endpoint is not properly initialized");
            float arrivalDilatedTime = destinationNode.networkEmulator.dilatedTimeNow + dilatedTimeDistance;
            var message = new Message(this, bytes.ToArray(), arrivalDilatedTime, inverseDilatedTimeDistance);
            destinationNode.networkEmulator.messages.Add(message);
        }
    }

    public class NetworkNode : INetwork
    {
        public string ConnectionString { get; }

        public readonly NetworkEmulator networkEmulator;

        Dictionary<string, RemoteEndpoint> remoteEndpoints = new Dictionary<string, RemoteEndpoint>();
        Queue<Message> queuedMessages = new Queue<Message>();

        // Initialized when some Unity object takes the ownership of this node.
        // The renderer is used to automatically position the nodeObject.
        private Renderer attachedRenderer;

        // Visually represents the connection point on the network graph.
        public GameObject nodeObject;

        public NetworkNode(NetworkEmulator networkEmulator, string connectionString)
        {
            this.networkEmulator = networkEmulator;
            ConnectionString = connectionString;
        }

        public bool IsAttached => attachedRenderer != null;

        public void AttachRenderer(Renderer attachedRenderer)
        {
            if (IsAttached)
                throw new Exception($"There is already a renderer attached to the network node '{ConnectionString}'");
            this.attachedRenderer = attachedRenderer;
            nodeObject = Instantiate(networkEmulator.endpointPrefab);
        }

        public void UpdateNodeObject(Vector3 graphCenter)
        {
            if (IsAttached)
                nodeObject.transform.position = attachedRenderer.bounds.ClosestPoint(graphCenter);
        }

        public void UpdateGraphCenter(ref Vector3 graphCenter, ref int nodesCount)
        {
            if (IsAttached)
            {
                graphCenter += attachedRenderer.bounds.center;
                ++nodesCount;
            }
        }

        public IRemoteEndpoint GetEndpoint(string connectionString)
        {
            RemoteEndpoint endpoint;
            if (!remoteEndpoints.TryGetValue(connectionString, out endpoint))
            {
                NetworkNode remoteNetworkNode = networkEmulator.GetOrCreateNetworkNode(connectionString);
                float dilatedTimeDistance = 1.0f;  // FIXME

                // Endpoints are created in pairs. One represents the other NetworkNode from the point of view of this one,
                // and another represents this NetworkNode from the point of view of the remote one.
                endpoint = new RemoteEndpoint(connectionString, remoteNetworkNode, dilatedTimeDistance);
                RemoteEndpoint twinEndpoint = new RemoteEndpoint(ConnectionString, this, dilatedTimeDistance);

                // When sending the messages to the endpoint, it will receive twinEndpoint
                // (pointing to this network) as a sender.
                endpoint.InitSender(twinEndpoint);
                twinEndpoint.InitSender(endpoint);
                remoteEndpoints.Add(connectionString, endpoint);
                remoteNetworkNode.remoteEndpoints.Add(ConnectionString, twinEndpoint);
            }
            return endpoint;
        }


        public bool PollMessage(OnMessage onMessage)
        {
            if (queuedMessages.Count == 0)
                return false;
            Message message = queuedMessages.Dequeue();
            onMessage(message.dstEndpoint.Sender, message.data);
            return true;
        }

        public NetworkNode(string connectionString)
        {
            ConnectionString = connectionString;
        }

        public static bool InterpolatePosition(NetworkNode a, NetworkNode b, float progress, out Vector3 result)
        {
            if (!a.IsAttached || !b.IsAttached)
            {
                result = new Vector3();
                return false;
            }
            result = Vector3.Lerp(a.nodeObject.transform.position, b.nodeObject.transform.position, progress);
            return true;
        }

        public GameObject CreateMessageGameObject()
        {
            return Instantiate(networkEmulator.messagePrefab);
        }

        public void EnqueueDeliveredMessage(Message message)
        {
            queuedMessages.Enqueue(message);
        }
    }


    public NetworkNode RegisterEndpoint(string connectionString, Renderer owningRenderer)
    {
        NetworkNode networkNode = GetOrCreateNetworkNode(connectionString);
        networkNode.AttachRenderer(owningRenderer);
        return networkNode;
    }

    public void OnTimeDilationSliderValueChanged(float value)
    {
        float arg = value * value;  // Quadratic to get more control over low values.
        timeDilation = Mathf.RoundToInt(Mathf.Lerp(minTimeDilation, maxTimeDilation, arg));
        timeDilationText.text = "Time dilation: x" + timeDilation.ToString();
    }

    public NetworkNode GetOrCreateNetworkNode(string connectionString)
    {
        NetworkNode networkNode;
        if (!networkNodes.TryGetValue(connectionString, out networkNode))
        {
            networkNode = new NetworkNode(this, connectionString);
            networkNodes.Add(connectionString, networkNode);
        }
        return networkNode;
    }

    void UpdateNodesPositions()
    {
        Vector3 graphCenter = new Vector3();
        int nodesCount = 0;
        foreach (NetworkNode networkNode in networkNodes.Values)
            networkNode.UpdateGraphCenter(ref graphCenter, ref nodesCount);
        if (nodesCount != 0)
        {
            graphCenter /= nodesCount;
            foreach (NetworkNode networkNode in networkNodes.Values)
                networkNode.UpdateNodeObject(graphCenter);
        }
    }

    void Start()
    {
        messagePrefab = Resources.Load<GameObject>("MessagePrefab");
        endpointPrefab = Resources.Load<GameObject>("EndpointPrefab");
    }

    void Update()
    {
        // No real reason to update this each frame, but doing this just for simplicity.
        UpdateNodesPositions();
        messages.RemoveAll(m => m.TryToDeliver(dilatedTimeNow));
    }
}

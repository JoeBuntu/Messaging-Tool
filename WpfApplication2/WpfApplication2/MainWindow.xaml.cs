using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.ServiceModel;
using System.Collections.ObjectModel;
using System.ServiceModel.Description;
using System.ServiceModel.Routing;
using System.ServiceModel.Dispatcher;
using System.ServiceModel.Channels;

namespace WpfApplication2
{ 
    public partial class MainWindow : Window, IMessageListener
    {
        private ServiceHost _RouterHost;
        private Uri service1Uri = new Uri("http://localhost/ServiceA/Service1.svc");
        private Uri service2Uri = new Uri("http://localhost/ServiceA/Service2.svc");
        private Uri routerUri = new Uri("http://localhost/routingservice/router");
        //private string Action = "http://tempuri.org/IService1/MyMethod";
        public ObservableCollection<MessageContainer> Messages { get; set; }
        private Dictionary<Uri, IRequestChannel> _Clients = new Dictionary<Uri, IRequestChannel>();

        private WSHttpBinding CreateBinding()
        {
            WSHttpBinding binding = new WSHttpBinding();
            binding.Security.Mode = SecurityMode.None;
            binding.ReliableSession.Enabled = false;
            return binding;
        }

        public MainWindow()
        {
            InitializeComponent();

            WSHttpBinding binding = CreateBinding();             
            IChannelListener<IReplyChannel> channel = binding.BuildChannelListener<IReplyChannel>(routerUri, new BindingParameterCollection());
            channel.Open();
            channel.BeginAcceptChannel(AcceptChannel, channel);

            Messages = new ObservableCollection<MessageContainer>();
            lbxMessages.ItemsSource = Messages; 

            //add endpoint for routing service

            //ServiceHost host = new ServiceHost(typeof(RoutingService), new Uri[] {});
            //host.Description.Behaviors.Add(new MyBehavior(this));
 
            //host.Open();
            //_RouterHost = host; 
        }

        public void AcceptChannel(IAsyncResult ar)
        {
            IChannelListener<IReplyChannel> listener = ar.AsyncState as IChannelListener<IReplyChannel>;
            IReplyChannel channel = listener.EndAcceptChannel(ar);
            channel.Open();
            channel.BeginReceiveRequest(AcceptRequest, channel);
        }

        public void AcceptRequest(IAsyncResult ar)
        {
            IReplyChannel channel = ar.AsyncState as IReplyChannel;
            RequestContext context = channel.EndReceiveRequest(ar);
            channel.BeginReceiveRequest(AcceptRequest, channel);
            ProcessRequest(context);
        }

        public void ProcessRequest(RequestContext context)
        { 
            MessageHeaders headers = context.RequestMessage.Headers;

            //create new request channel if needed
            IRequestChannel requestChannel = null;
            if (!_Clients.TryGetValue(headers.To, out requestChannel))
            {
                WSHttpBinding binding = CreateBinding();
                IChannelFactory<IRequestChannel> factory = binding.BuildChannelFactory<IRequestChannel>(new BindingParameterCollection());
                factory.Open();

                requestChannel = factory.CreateChannel(new EndpointAddress(headers.To));
                requestChannel.Open();
                _Clients.Add(headers.To, requestChannel);
            }


            Message incoming = CopyMessage(context.RequestMessage, MessageDirection.Incoming);
            MessageVersion version = context.RequestMessage.Version;
            string action = headers.Action;
            Message requestMessage = Message.CreateMessage(context.RequestMessage.Version, headers.Action, incoming.GetReaderAtBodyContents());

            Message responseMessage = requestChannel.Request(requestMessage);
            Message outgoing = CopyMessage(responseMessage, MessageDirection.Outgoing);
            context.Reply(outgoing);
        }

        public Message CopyMessage(Message message, MessageDirection direction)
        {
            MessageBuffer buffer = message.CreateBufferedCopy(Int32.MaxValue);

            MessageContainer container = new MessageContainer();
            container.Message = buffer.CreateMessage();
            container.MessageText = buffer.CreateMessage().ToString();
            container.Received = DateTime.Now;
            container.IsIncoming = (direction == MessageDirection.Incoming);
           
            Action<MessageContainer> msg = new Action<MessageContainer>(Messages.Add);
            this.Dispatcher.Invoke(msg, container);
            return buffer.CreateMessage();
        }


        public void MessageRecieved(Message message, MessageDirection direction)
        { 
            MessageContainer container = new MessageContainer();
            container.Message = message.CreateBufferedCopy(Int32.MaxValue).CreateMessage();
            container.MessageText = message.ToString();
            container.Received = DateTime.Now;
            container.IsIncoming = (direction == MessageDirection.Incoming);
           
            Action<MessageContainer> msg = new Action<MessageContainer>(Messages.Add);
            this.Dispatcher.Invoke(msg, container);
        }
    }

    [ServiceContract]
    public class SomeService
    {
        [OperationContract]
        public void DoSomething()
        {
            
        }
    }

    public class MessageContainer
    {
        public DateTime Received { get; set; }
        public Message Message { get; set; }
        public string MessageText { get; set; }
        public bool IsIncoming { get; set; }
    }

    public interface IMessageListener
    {
        void MessageRecieved(Message message, MessageDirection direction);
    }

    public enum MessageDirection
    {
        Incoming,
        Outgoing
    }
    public class MyBehavior :IServiceBehavior, IEndpointBehavior
    {
        private readonly IMessageListener _MessageListener;
        public MyBehavior(IMessageListener messageListener)
        {
            _MessageListener = messageListener;
        }

        #region IServiceBehavior
         
        public void AddBindingParameters(ServiceDescription serviceDescription, ServiceHostBase serviceHostBase, Collection<ServiceEndpoint> endpoints, System.ServiceModel.Channels.BindingParameterCollection bindingParameters)
        { 
            // Do Nothing
        }
        public void Validate(ServiceDescription serviceDescription, ServiceHostBase serviceHostBase)
        {
            // Do Nothing
        }

        public void ApplyDispatchBehavior(ServiceDescription serviceDescription, ServiceHostBase serviceHostBase)
        {
            foreach (ServiceEndpoint endpoint in serviceDescription.Endpoints)
            {
                endpoint.Behaviors.Add(this);
            }
        }

        #endregion

        #region IEndpointBehavior

        public void AddBindingParameters(ServiceEndpoint endpoint, System.ServiceModel.Channels.BindingParameterCollection bindingParameters)
        { 
            // Do nothing
        }
        public void Validate(ServiceEndpoint endpoint)
        {
            // Do nothing
        }
        public void ApplyClientBehavior(ServiceEndpoint endpoint, ClientRuntime clientRuntime)
        {
            throw new NotImplementedException();
        }
        public void ApplyDispatchBehavior(ServiceEndpoint endpoint, EndpointDispatcher endpointDispatcher)
        {
            endpointDispatcher.AddressFilter = new MatchAllMessageFilter();
            endpointDispatcher.DispatchRuntime.MessageInspectors.Add(new MyMessageInspector(_MessageListener));
        }

        #endregion

    }

    public class MyMessageInspector : IDispatchMessageInspector
    {
        private readonly IMessageListener _MessageListener;
        public MyMessageInspector(IMessageListener messageListener)
        {
            _MessageListener = messageListener;
        }

        public object AfterReceiveRequest(ref Message request, IClientChannel channel, InstanceContext instanceContext)
        {
            MessageBuffer buffer = request.CreateBufferedCopy(Int32.MaxValue);
            _MessageListener.MessageRecieved(buffer.CreateMessage(), MessageDirection.Incoming);
            request = buffer.CreateMessage();
            return null;
        }

        public void BeforeSendReply(ref Message reply, object correlationState)
        {
            MessageBuffer buffer = reply.CreateBufferedCopy(Int32.MaxValue);
            _MessageListener.MessageRecieved(buffer.CreateMessage(), MessageDirection.Outgoing);
            reply = buffer.CreateMessage();
        }
    }
}

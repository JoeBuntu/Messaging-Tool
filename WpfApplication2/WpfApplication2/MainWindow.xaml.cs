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
    public partial class MainWindow : Window
    { 
        private Uri routerUri = new Uri("http://localhost/routingservice/router");
        public ObservableCollection<MessageContainer> Messages { get; set; }
        private Dictionary<Uri, IRequestChannel> _Clients = new Dictionary<Uri, IRequestChannel>();

        private WSHttpBinding CreateBinding()
        {
            return new WSHttpBinding("default");
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
            try
            { 
                RequestContext context = channel.EndReceiveRequest(ar);
                channel.BeginReceiveRequest(AcceptRequest, channel);
                ProcessRequest(context);
            }
            catch (TimeoutException)
            {
                channel.BeginReceiveRequest(AcceptRequest, channel);
            }     
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

            //forward request and send back response
            MessageContainer container = new MessageContainer();
            Message incoming = CopyMessage(context.RequestMessage, container, MessageDirection.Incoming);

            //add request to gui
            Action<int, MessageContainer> msg = new Action<int, MessageContainer>(Messages.Insert);
            this.Dispatcher.Invoke(msg, new object[] { 0, container });

            //get response
            Message responseMessage = requestChannel.Request(incoming);
            Message outgoing = CopyMessage(responseMessage, container, MessageDirection.Outgoing);
            context.Reply(outgoing);
        }

        public Message CopyMessage(Message message, MessageContainer container, MessageDirection direction)
        {
            MessageBuffer buffer = message.CreateBufferedCopy(Int32.MaxValue);

            if (direction == MessageDirection.Incoming)
            {
                container.RequestMessageText = buffer.CreateMessage().ToString();
                container.RequestReceived = DateTime.Now;
                if (message.Properties.ContainsKey(RemoteEndpointMessageProperty.Name))
                {
                    RemoteEndpointMessageProperty property = (RemoteEndpointMessageProperty)message.Properties[RemoteEndpointMessageProperty.Name];
                    container.RemoteAddress = property.Address;
                }
                if (message.Headers.To != null)
                {
                    container.ToAddress = message.Headers.To.ToString();
                }
                container.RequestAction = message.Headers.Action;
            } 
            else if(direction == MessageDirection.Outgoing)
            {
                container.ResponseMessageText = buffer.CreateMessage().ToString();
                container.ResponseReceived = DateTime.Now;
                container.ResponseAction = message.Headers.Action;
            }
            return buffer.CreateMessage();
        } 
    }
 
    public class MessageContainer
    {
        public DateTime RequestReceived { get; set; }
        public DateTime ResponseReceived { get; set; }
        public Message RequestMessage { get; set; }
        public Message ResponseMessage { get; set; }
        public string RequestMessageText { get; set; }
        public string ResponseMessageText { get; set; }
        public string RequestAction { get; set; }
        public string ResponseAction { get; set; }
        public string RemoteAddress { get; set; }
        public string ToAddress { get; set; }
        public long TotalLength { get { return RequestMessageText.Length + ResponseMessageText.Length; } }
        public double Duration { get { return (ResponseReceived - RequestReceived).TotalMilliseconds; } }
    }
 
    public enum MessageDirection
    {
        Incoming, Outgoing
    } 
}

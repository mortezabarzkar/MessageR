﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace MessageR
{
	/// <summary>
	/// Provides brokering of messages between code sending and listening to messages within an actor-based architecture.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Messages must inherit from the <see cref="MessageR.Message"/> type and are sent via dispatchers, which implement the <see cref="MessageR.IMessageDispatcher"/> interface.
	/// </para>
	/// <para>
	/// A dispatcher is added to the broker using the <see cref="AddDispatcher()"/> methods. 
	/// A <see cref="System.Predicate{Message}"/> may be used to filter which messages are sent to dispatchers.
	/// </para>
	/// <para>
	/// Messages may be sent through the broker using the <see cref="Send()"/> and <see cref="SendAsync()"/> methods.
	/// A <see cref="MessageR.MessageToken"/> is returned, which may be used to await completion or a response to the message.
	/// </para>
	/// <para>
	/// Messages may be received into the broker for processing to listeners using the <see cref="Receive()"/> and <see cref="ReceiveAsync()"/> methods.
	/// </para>
	/// <para>
	///	Clients may listen for messages of particular types using the <see cref="Listen"/> methods.
	///	A <see cref="MessageR.ListenerToken"/> is returned, which may be used to Stop listening for messages.
	/// </para>
	/// </remarks>
	public class MessageBroker : IMessageBroker
	{
		//////////////////////////////////////////////////////////////////////

		#region Members

		/// <summary>
		/// Block to send messages from
		/// </summary>
		private BroadcastBlock<Message> sendBlock
			= new BroadcastBlock<Message>(Functions.Identity);

		/// <summary>
		/// Block to receive messages into
		/// </summary>
		private BroadcastBlock<Message> receiveBlock
			= new BroadcastBlock<Message>(Functions.Identity);

		/// <summary>
		/// References to disposable objects for our dispatchers
		/// </summary>
		private ConcurrentDictionary<IMessageDispatcher, IDisposable> dispatchers
			= new ConcurrentDictionary<IMessageDispatcher, IDisposable>();

		/// <summary>
		/// Lazy instance for Singleton use
		/// </summary>
		private static readonly Lazy<MessageBroker> instance =
			new Lazy<MessageBroker>();

		#endregion

		//////////////////////////////////////////////////////////////////////

		#region Properties

		/// <summary>
		/// Gets a singleton instance of the MessageBroker
		/// </summary>
		public static MessageBroker Instance
		{
			get
			{
				return instance.Value;
			}
		}

		#endregion

		//////////////////////////////////////////////////////////////////////

		#region Constructors

		/// <summary>
		/// Initialises a new instance of the MessageBroker class
		/// </summary>
		public MessageBroker()
		{
			// Add a default null handler for unknown messages
			sendBlock.LinkTo(DataflowBlock.NullTarget<Message>());
			receiveBlock.LinkTo(DataflowBlock.NullTarget<Message>());
		}

		#endregion

		//////////////////////////////////////////////////////////////////////

		#region Public Methods

		/// <summary>
		/// Adds a dispatcher to the broker which handles all messages
		/// </summary>
		/// <param name="dispatcher">The dispatcher to add</param>
		public void AddDispatcher(IMessageDispatcher dispatcher)
		{
			if (dispatcher == null) throw new ArgumentNullException("dispatcher");

			if (dispatchers.ContainsKey(dispatcher))
			{
				throw new Exception("Dispatcher has already been added");
			}

			ActionBlock<Message> dispatcherBlock = new ActionBlock<Message>(m => dispatcher.DispatchAsync(m));
			dispatchers.TryAdd(dispatcher, sendBlock.LinkTo(dispatcherBlock));				
		}

		/// <summary>
		/// Adds a dispatcher to the broker which handles all message types
		/// </summary>
		/// <param name="dispatcher"></param>
		public void AddDispatcher(IMessageDispatcher dispatcher, Predicate<Message> predicate)
		{
			if (dispatcher == null) throw new ArgumentNullException("dispatcher");
			if (predicate == null) throw new ArgumentNullException("predicate");

			if (dispatchers.ContainsKey(dispatcher))
			{
				throw new Exception("Dispatcher has already been added");
			}

			ActionBlock<Message> dispatcherBlock = new ActionBlock<Message>(m => dispatcher.DispatchAsync(m));
			dispatchers.TryAdd(dispatcher, sendBlock.LinkTo(dispatcherBlock, predicate));
		}

		/// <summary>
		/// Removes a dispatcher from the broker
		/// </summary>
		/// <param name="dispatcher">The dispatcher to remove</param>
		public void RemoveDispatcher(IMessageDispatcher dispatcher)
		{
			if (dispatcher == null) throw new ArgumentNullException("dispatcher");

			IDisposable dispatcherRef;

			if (dispatchers.TryRemove(dispatcher, out dispatcherRef))
			{
				dispatcherRef.Dispose();
			}
		}

		/// <summary>
		/// Sends a message through the broker
		/// </summary>
		/// <typeparam name="T">Must inherit from <see cref="Message"/></typeparam>
		/// <param name="message">The message to be sent</param>
		/// <returns>A <see cref="MessageToken"/> which allows interaction with the sent message</returns>
		public MessageToken Send<T>(T message) where T : Message
		{
			if (message == null) throw new ArgumentNullException("message");

			sendBlock.Post(message);

			return new MessageToken(this, message);
		}

		/// <summary>
		/// Sends a message through the broker asyncronously
		/// </summary>
		/// <typeparam name="T">Must inherit from <see cref="Message"/></typeparam>
		/// <param name="message">The message to be sent</param>
		/// <returns>A <see cref="MessageToken"/> which allows interaction with the sent message</returns>
		public async Task<MessageToken> SendAsync<T>(T message) where T : Message
		{
			if (message == null) throw new ArgumentNullException("message");

			await sendBlock.SendAsync(message);

			return new MessageToken(this, message);
		}

		/// <summary>
		/// Receives a message into the broker
		/// </summary>
		/// <param name="message"></param>
		public bool Receive(Message message)
		{
			if (message == null) throw new ArgumentNullException("message");

			return receiveBlock.Post(message);
		}

		/// <summary>
		/// Receives a message into the broker asynchronously
		/// </summary>
		/// <param name="message"></param>
		public async Task<bool> ReceiveAsync(Message message)
		{
			if (message == null) throw new ArgumentNullException("message");

			return await receiveBlock.SendAsync(message);
		}

		/// <summary>
		/// Listens to a message of type T, providing an awaitable handler for the message
		/// </summary>
		/// <typeparam name="T">The type of message to listen to</typeparam>
		/// <param name="handler">The handler for messages received</param>
		/// <returns>A <see cref="ListenerToken"/> which can be used to stop listening</returns>
		public ListenerToken Listen<T>(Func<T, Task> handler) where T : Message
		{
			if (handler == null) throw new ArgumentNullException("handler");

			return Listen<T>(Functions.True, handler);
		}

		/// <summary>
		/// Listens to a message of type T matching the given <see cref="Predicate{T}"/>, providing an awaitable handler for the message
		/// </summary>
		/// <typeparam name="T">The type of message to listen to</typeparam>
		/// <param name="handler">The handler for messages received</param>
		/// <param name="predicate">The Predicate used to match the received message to the listener</param>
		/// <returns>A <see cref="ListenerToken"/> which can be used to stop listening</returns>
		public ListenerToken Listen<T>(Predicate<T> predicate, Func<T, Task> handler) where T : Message
		{
			if (handler == null) throw new ArgumentNullException("handler");

			DisposableChain disposableChain = new DisposableChain();

			TransformBlock<Message, T> transformBlock = new TransformBlock<Message, T>(m => (T)m);
			ActionBlock<T> handlerBlock = new ActionBlock<T>(t =>
			{
				try
				{
					handler(t);
				}
				catch (Exception ex)
				{
					RaiseExceptionMessage(t, ex);
				}
			});
			disposableChain.AddDisposable(transformBlock.LinkTo(handlerBlock, predicate));
			disposableChain.AddDisposable(receiveBlock.LinkTo(transformBlock, m => typeof(T).IsAssignableFrom(m.GetType())));

			return new ListenerToken(new IDataflowBlock[] { transformBlock, handlerBlock }, disposableChain);
		}

		/// <summary>
		/// Listens to a message of type T, providing a handler for the message
		/// </summary>
		/// <typeparam name="T">The type of message to listen to</typeparam>
		/// <param name="handler">The handler for messages received</param>
		/// <returns>A <see cref="ListenerToken"/> which can be used to stop listening</returns>
		public ListenerToken Listen<T>(Action<T> handler) where T : Message
		{
			if (handler == null) throw new ArgumentNullException("handler");

			return Listen<T>(Functions.True, handler);		
		}

		/// <summary>
		/// Listens to a message of type T matching the given <see cref="Predicate{T}"/>, providing a handler for the message
		/// </summary>
		/// <typeparam name="T">The type of message to listen to</typeparam>
		/// <param name="handler">The handler for messages received</param>
		/// <param name="predicate">The Predicate used to match the received message to the listener</param>
		/// <returns>A <see cref="ListenerToken"/> which can be used to stop listening</returns>
		public ListenerToken Listen<T>(Predicate<T> predicate, Action<T> handler) where T : Message
		{
			if (handler == null) throw new ArgumentNullException("handler");

			DisposableChain disposableChain = new DisposableChain();

			TransformBlock<Message, T> transformBlock = new TransformBlock<Message, T>(m => (T)m);
			ActionBlock<T> handlerBlock = new ActionBlock<T>(t =>
			{
				try
				{
					handler(t);
				}
				catch (Exception ex)
				{
					RaiseExceptionMessage(t, ex);
				}
			});
			disposableChain.AddDisposable(transformBlock.LinkTo(handlerBlock, predicate));
			disposableChain.AddDisposable(receiveBlock.LinkTo(transformBlock, m => typeof(T).IsAssignableFrom(m.GetType())));

			return new ListenerToken(new IDataflowBlock[] { transformBlock, handlerBlock }, disposableChain);
		}

		/// <summary>
		/// Listens for message(s) matching the given <see cref="System.Predicate{Message}"/>, providing a handler for messages received
		/// </summary>
		/// <param name="predicate">The Predicate used to match the received message to the listener</param>
		/// <param name="handler">The handler for messages received</param>
		public ListenerToken Listen(Predicate<Message> predicate, Action<Message> handler)
		{
			if (predicate == null) throw new ArgumentNullException("predicate");
			if (handler == null) throw new ArgumentNullException("handler");

			ActionBlock<Message> handlerBlock = new ActionBlock<Message>(m =>
			{
				try
				{
					handler(m);
				}
				catch (Exception ex)
				{
					RaiseExceptionMessage(m, ex);
				}
			});

			return new ListenerToken(new IDataflowBlock[] { handlerBlock}, receiveBlock.LinkTo(handlerBlock, predicate));
		}

		/// <summary>
		/// Listens for message(s) matching the given <see cref="System.Predicate{Message}"/>, providing an awaitable handler for messages received
		/// </summary>
		/// <param name="predicate">The Predicate used to match the received message to the listener</param>
		/// <param name="handler">The handler for messages received</param>
		public ListenerToken Listen(Predicate<Message> predicate, Func<Message, Task> handler)
		{
			if (predicate == null) throw new ArgumentNullException("predicate");
			if (handler == null) throw new ArgumentNullException("handler");

			ActionBlock<Message> handlerBlock = new ActionBlock<Message>(m =>
			{
				try
				{
					handler(m);
				}
				catch (Exception ex)
				{
					RaiseExceptionMessage(m, ex);
				}
			});

			return new ListenerToken(new IDataflowBlock[] { handlerBlock }, receiveBlock.LinkTo(handlerBlock, predicate));
		}

		#endregion

		//////////////////////////////////////////////////////////////////////

		#region Private Methods

		/// <summary>
		/// Raises an exception message based on the message and exception supplied
		/// </summary>
		/// <param name="message">The message being handled at the time of the exception</param>
		/// <param name="exception">The exception to raise</param>
		private void RaiseExceptionMessage(Message message, Exception exception)
		{
			Send<Message>(new Message(message, exception));
		}

		#endregion

		//////////////////////////////////////////////////////////////////////
	}
}

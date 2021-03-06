﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MessageR
{
	/// <summary>
	/// Represents a message with a generic typed payload
	/// </summary>
	/// <typeparam name="T"></typeparam>
	public class Message<T> : Message
	{
		//////////////////////////////////////////////////////////////////////

		#region Members

		public T Payload
		{
			get;
			private set;
		}

		#endregion

		//////////////////////////////////////////////////////////////////////

		#region Constructors

		/// <summary>
		/// Initialises a new instance of the Message{T} class
		/// </summary>
		/// <param name="payload"></param>
		public Message(T payload)
		{
			if (payload == null) throw new ArgumentNullException("payload");

			this.Payload = payload;
		}

		/// <summary>
		/// Initialises a new instance of the Message{T} class
		/// </summary>
		/// <param name="payload"></param>
		public Message(T payload, Message referenceMessage)
			: base(referenceMessage)
		{
			if (payload == null) throw new ArgumentNullException("payload");

			this.Payload = payload;
		}

		#endregion

		//////////////////////////////////////////////////////////////////////
	}
}

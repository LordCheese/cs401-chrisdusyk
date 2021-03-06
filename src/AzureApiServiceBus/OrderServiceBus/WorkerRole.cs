﻿using Microsoft.Azure;
using Microsoft.ServiceBus;
using Microsoft.ServiceBus.Messaging;
using Microsoft.WindowsAzure.ServiceRuntime;
using OrderServiceBus.Models;
using System;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Net.Mail;
using CS401DataContract;
using Newtonsoft.Json;

namespace OrderServiceBus
{
	public class WorkerRole : RoleEntryPoint
	{
		// The name of your queue
		private const string QueueName = "cs401queue";

		// QueueClient is thread-safe. Recommended that you cache
		// rather than recreating it on every request
		private QueueClient Client;

		private ManualResetEvent CompletedEvent = new ManualResetEvent(false);

		public override void Run()
		{
			Trace.WriteLine("Starting processing of messages");

			// Initiates the message pump and callback is invoked for each message that is received, calling close on the client will stop the pump.
			Client.OnMessage((receivedMessage) =>
				{
					try
					{
						// Process the message
						Trace.WriteLine("Processing Service Bus message: " + receivedMessage.SequenceNumber.ToString());

						try
						{
							// Parse the object from the message
							string jsonOrder = receivedMessage.GetBody<string>();
							receivedMessage.Complete();

							// Handle timezone information. Client will send it in their local time. DateTimeZoneHandling.RoundtripKind maintains the timezone information in the DateTime 
							// so it can be easily converted to the client's local time later on.
							JsonSerializerSettings jsonSettings = new JsonSerializerSettings()
							{
								DateTimeZoneHandling = DateTimeZoneHandling.RoundtripKind
							};

							// Deserialize JSON dictionary of PackagedOrder object
							PackagedOrder order = JsonConvert.DeserializeObject<PackagedOrder>(jsonOrder);

							// Call the method to insert the data into the database
							InsertPackagedOrder(order);
						}
						catch (Exception ex)
						{
							SendErrorMessage(ex);
						}
					}
					catch
					{
						// Handle any message processing specific exceptions here
						Trace.WriteLine("Error retrieving message: " + receivedMessage.SequenceNumber);
					}
				});

			CompletedEvent.WaitOne();
		}

		public override bool OnStart()
		{
			// Set the maximum number of concurrent connections
			ServicePointManager.DefaultConnectionLimit = 12;

			// Create the queue if it does not exist already
			string connectionString = CloudConfigurationManager.GetSetting("Microsoft.ServiceBus.ConnectionString");
			var namespaceManager = NamespaceManager.CreateFromConnectionString(connectionString);
			if (!namespaceManager.QueueExists(QueueName))
			{
				namespaceManager.CreateQueue(QueueName);
			}

			// Initialize the connection to Service Bus Queue
			Client = QueueClient.CreateFromConnectionString(connectionString, QueueName);
			return base.OnStart();
		}

		public override void OnStop()
		{
			// Close the connection to Service Bus Queue
			Client.Close();
			CompletedEvent.Set();
			base.OnStop();
		}

		/// <summary>
		/// Insert the Order and OrderProducts into the database.
		/// </summary>
		/// <param name="packagedOrder">PackagedOrder object received from the Service Bus.</param>
		private void InsertPackagedOrder(PackagedOrder packagedOrder)
		{
			try
			{
				// Create the database connection and context
				// Since it's in a using-block, it will automatically dispose of the connection when it exits the block
				using (CS401_DBEntities1 db = new CS401_DBEntities1())
				{
					var order = packagedOrder.Order;

					// Add the order to the db context
					db.Orders.Add(order);

					// Insert order to get an OrderID
					db.SaveChanges();

					foreach (var orderProduct in packagedOrder.OrderProducts)
					{
						orderProduct.OrderId = order.OrderId;

						db.OrderProducts.Add(orderProduct);
					}

					// Insert OrderProducts into database now
					db.SaveChanges();
				}
			}
			catch (Exception ex)
			{
				Trace.WriteLine("Error inserting data into db: " + ex.Message);
				SendErrorMessage(ex);
			}
		}

		/// <summary>
		/// Send an error report email to me, for debugging issues for the project.
		/// </summary>
		/// <param name="ex">Exception caught by the try-catch block.</param>
		private void SendErrorMessage(Exception ex)
		{
			var message = new SendGrid.SendGridMessage();
			message.AddTo("chris.dusyk@gmail.com");
			message.From = new MailAddress("chris.dusyk@gmail.com", "Service Bus Error");
			message.Subject = "Service Bus Error";
			message.Text = ex.Message;

			var transport = new SendGrid.Web("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");
			transport.DeliverAsync(message).Wait();
		}
	}
}
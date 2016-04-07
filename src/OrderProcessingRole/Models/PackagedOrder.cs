﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace OrderProcessingRole.Models
{
	public class PackagedOrder
	{
		public Order Order { get; set; }

		public List<OrderProduct> OrderProducts { get; set; }
	}
}
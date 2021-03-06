﻿using System;
using System.ComponentModel;
using SoundInTheory.DynamicImage.Util;

namespace SoundInTheory.DynamicImage.Sources
{
	public class BytesImageSource : ImageSource
	{
		[Category("Source"), Browsable(false)]
		public byte[] Bytes
		{
			get
			{
				object value = this.ViewState["Bytes"];
				if (value != null)
					return (byte[]) value;
				return null;
			}
			set
			{
				this.ViewState["Bytes"] = value;
			}
		}

		public override FastBitmap GetBitmap(ISite site, bool designMode)
		{
			byte[] bytes = this.Bytes;
			if (bytes != null && bytes.Length > 0)
				return new FastBitmap(bytes);
			return null;
		}
	}
}

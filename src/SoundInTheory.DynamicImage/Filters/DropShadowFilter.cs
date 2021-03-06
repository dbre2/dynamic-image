﻿using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using SoundInTheory.DynamicImage.Util;

namespace SoundInTheory.DynamicImage.Filters
{
	public class DropShadowFilter : ShadowFilterBase
	{
		#region Properties

		/// <summary>
		/// Gets or sets the angle of the light source. The angle is measured counter-clockwise from the x-axis.
		/// </summary>
		[DefaultValue(315)]
		public int Angle
		{
			get
			{
				object value = this.ViewState["Angle"];
				if (value != null)
					return (int) value;
				return 315;
			}
			set
			{
				if (value < 0 || value > 360)
					throw new ArgumentException("value", "The angle must be between 0 and 360 degrees.");

				this.ViewState["Angle"] = value;
			}
		}

		/// <summary>
		/// Gets or sets the offset for the shadow.
		/// </summary>
		[DefaultValue(5)]
		public int Distance
		{
			get
			{
				object value = this.ViewState["Distance"];
				if (value != null)
					return (int) value;
				return 5;
			}
			set
			{
				if (value < 0)
					throw new ArgumentOutOfRangeException("value", "The offset of the shadow must be greater than or equal to zero.");

				this.ViewState["Distance"] = value;
			}
		}

		#endregion

		#region Methods

		protected override Vector GetPadding()
		{
			Vector offsetSize = GetOffsetSize();
			offsetSize.X = (int) Math.Ceiling(Math.Abs(offsetSize.X));
			offsetSize.Y = (int)Math.Ceiling(Math.Abs(offsetSize.Y));
			offsetSize.X += Size;
			offsetSize.Y += Size;
			return offsetSize;
		}

		private Vector GetOffsetSize()
		{
			float radians = MathUtility.ToRadians(Angle + 180);
			Vector result = new Vector(
				Math.Cos(radians) * Distance,
				-Math.Sin(radians) * Distance);
			return result;
		}

		protected override Effect GetEffect(FastBitmap source)
		{
			return new DropShadowEffect
			{
				Direction = Angle,
				Color = Color,
				BlurRadius = Size,
				ShadowDepth = Distance,
				Opacity = Opacity
			};
		}

		protected override void ApplyFilter(FastBitmap source, DrawingContext dc, int width, int height)
		{
			Vector offsetSize = GetOffsetSize() + new Vector(Size, Size);
			dc.PushTransform(new TranslateTransform(offsetSize.X, offsetSize.Y));
			base.ApplyFilter(source, dc, width, height);
			dc.Pop();
		}

		#endregion
	}
}

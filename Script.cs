#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

//This namespace holds Strategies in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.Strategies
{
	public class ORB : Strategy
	{
		private double BoxHigh;
		private double BoxLow;
		private double BoxHeight;
		private bool SessionTradeTaken;
		private DateTime SessionOpen;
		private bool IsLondonSession;


		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description									= @"Enter the description for your new custom Strategy here.";
				Name										= "ORB";
				Calculate									= Calculate.OnBarClose;
				EntriesPerDirection							= 1;
				EntryHandling								= EntryHandling.AllEntries;
				IsExitOnSessionCloseStrategy				= true;
				ExitOnSessionCloseSeconds					= 30;
				IsFillLimitOnTouch							= false;
				MaximumBarsLookBack							= MaximumBarsLookBack.TwoHundredFiftySix;
				OrderFillResolution							= OrderFillResolution.Standard;
				Slippage									= 0;
				StartBehavior								= StartBehavior.WaitUntilFlat;
				TimeInForce									= TimeInForce.Gtc;
				TraceOrders									= false;
				RealtimeErrorHandling						= RealtimeErrorHandling.StopCancelClose;
				StopTargetHandling							= StopTargetHandling.PerEntryExecution;
				BarsRequiredToTrade							= 20;
				// Disable this property for performance gains in Strategy Analyzer optimizations
				// See the Help Guide for additional information
				IsInstantiatedOnEachOptimizationIteration	= true;
				Session1Hour					= 2;
				Session1Min					= 0;
				Session2Hour					= 8;
				Session2Min					= 30;
				MinBoxLondon					= 0.75;
				MinBoxNewYork					= 1;
				MaxBox					= 8;
				VolMultiplierLon					= 1.15;
				VolMultiplierNY					= 1.3;
				BoxHigh					= 0;
				BoxLow					= 0;
				BoxHeight					= 0;
				SessionTradeTaken					= false;
				SessionOpen						= DateTime.Parse("00:00", System.Globalization.CultureInfo.InvariantCulture);
				IsLondonSession					= false;
			}
			else if (State == State.Configure)
			{
			}
		}

		protected override void OnBarUpdate()
		{
			// Dont start any logic until you've got at least 20 bars
			if (BarsInProgress != 0 || CurrentBar < BarsRequiredToTrade) 
				return;

			// saves the current time on 'now' variable on every bar update
			DateTime now = Time[0];
			
			// 1) Session detection and reset
			// London start
			// When bar closes on specified time, session is detected
			if(now.Hour == Session1Hour && now.Minute == Session1Min)
			{
				Print("London open detected at " + now);
				SessionOpen = now;
				IsLondonSession = true;
				SessionTradeTaken = false;
				
				// Clear any old drawings
				BoxHigh = 0;
				BoxLow = 0;
				RemoveDrawObject("BoxHigh");
				RemoveDrawObject("BoxLow");
			}
			
			
			// New York Start
			else if(now.Hour == Session2Hour && now.Minute == Session2Min) 
			{
				Print("New York open detected at " + now);
				SessionOpen        = now;
		        IsLondonSession    = false;
		        SessionTradeTaken  = false;
		        BoxHigh = 0;
		        BoxLow  = 0;
		        RemoveDrawObject("BoxHigh");
		        RemoveDrawObject("BoxLow");
			}
			
			
			// 2) Five minutes after session open -> define the box
			if(SessionOpen != default(DateTime) && now == SessionOpen.AddMinutes(5)) 
			{
				Print("Drawing ORB box at " + now + $"  High={High[0]} Low={Low[0]}");
				// Gets the high and low. High[int barsAgo]
				BoxHigh = High[0];
				BoxLow = Low[0];
				BoxHeight = BoxHigh - BoxLow;
				
				// Now draw the horizontal lines on your chart
				Draw.Line(this, "BoxHigh", 0, BoxHigh, 5, BoxHigh, Brushes.Red);
				Draw.Line(this, "BoxLow", 0, BoxLow, 5, BoxLow, Brushes.Red);
			}
			
			// to work on next
			
		}

		#region Properties
		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name="Session1Hour", Description="London open hour (CT)", Order=1, GroupName="Parameters")]
		public int Session1Hour
		{ get; set; }

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name="Session1Min", Description="London open minute", Order=2, GroupName="Parameters")]
		public int Session1Min
		{ get; set; }

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name="Session2Hour", Description="New York open hour (CT)", Order=3, GroupName="Parameters")]
		public int Session2Hour
		{ get; set; }

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name="Session2Min", Description="New York Open Minute", Order=4, GroupName="Parameters")]
		public int Session2Min
		{ get; set; }

		[NinjaScriptProperty]
		[Range(0, double.MaxValue)]
		[Display(Name="MinBoxLondon", Description="Minimum box height for London (pts)", Order=5, GroupName="Parameters")]
		public double MinBoxLondon
		{ get; set; }

		[NinjaScriptProperty]
		[Range(0, double.MaxValue)]
		[Display(Name="MinBoxNewYork", Description="Minimum box height for NY (pts)", Order=6, GroupName="Parameters")]
		public double MinBoxNewYork
		{ get; set; }

		[NinjaScriptProperty]
		[Range(0, double.MaxValue)]
		[Display(Name="MaxBox", Description="Maximum box height for both sessions", Order=7, GroupName="Parameters")]
		public double MaxBox
		{ get; set; }

		[NinjaScriptProperty]
		[Range(0, double.MaxValue)]
		[Display(Name="VolMultiplierLon", Description="Volume multiple required in London", Order=8, GroupName="Parameters")]
		public double VolMultiplierLon
		{ get; set; }

		[NinjaScriptProperty]
		[Range(0, double.MaxValue)]
		[Display(Name="VolMultiplierNY", Description="Volume multiple required in New York", Order=9, GroupName="Parameters")]
		public double VolMultiplierNY
		{ get; set; }
		#endregion

	}
}

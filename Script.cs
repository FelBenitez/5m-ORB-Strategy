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
		private double PrevDayHigh, PrevDayLow;


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
				AddDataSeries(BarsPeriodType.Day, 1);
			}
		}

		protected override void OnBarUpdate()
		{
			// Step 0, handle daily series
			if(BarsInProgress == 1) 
			{
				if(CurrentBar > 1)
				{
					PrevDayHigh = Highs[1][1];
					PrevDayLow  = Lows[1][1];
            		RemoveDrawObject("PD_High");
            		RemoveDrawObject("PD_Low");
            		Draw.HorizontalLine(this, "PD_High", PrevDayHigh, Brushes.Blue, DashStyleHelper.Dash, 2, true);
            		Draw.HorizontalLine(this, "PD_Low", PrevDayLow,  Brushes.Blue, DashStyleHelper.Dash, 2, true);
				}
				
			}
			
			// Guard the 5-minute series
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
				SessionTradeTaken = false; // So it resets in case it's still true from previous session
				
				// Clear any old drawings
				BoxHigh = 0;
				BoxLow = 0;
				BoxHeight = 0;
				RemoveDrawObject("BoxHigh");
				RemoveDrawObject("BoxLow");
			}
			
			
			// New York Start
			else if(now.Hour == Session2Hour && now.Minute == Session2Min) 
			{
				Print("New York open detected at " + now);
				SessionOpen        = now;
		        IsLondonSession    = false;
		        SessionTradeTaken  = false; // So it resets in case it's still true from previous session
		        BoxHigh = 0;
		        BoxLow  = 0;
				BoxHeight = 0;
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
				BoxHeight = BoxHigh - BoxLow; // Calculates box height only after 5-min candle has occurred
				
				Print($"Drawing ORB box at {now}  High={BoxHigh:F2} Low={BoxLow:F2}");
				
				// Now draw the horizontal lines on your chart
				Draw.HorizontalLine(this, "BoxHigh", BoxHigh, Brushes.Red, DashStyleHelper.Solid, 2, true);
				Draw.HorizontalLine(this, "BoxLow", BoxLow, Brushes.Red, DashStyleHelper.Solid, 2, true);
			}
			
			// 3) Check if the candles meet the conditions for a valid breakout ONLY after first 5-min has been drawn out
			if(!SessionTradeTaken && BoxHeight > 0) // If box height > 0 then it has already been drawn out
			{
				// Step 1, breakout detection. most efficient step first before checking for more.
				bool longBreakout = Close[0] > BoxHigh;
				bool shortBreakout = Close[0] < BoxLow;
				if(longBreakout)
					Print("Breakout LONG at " + now);
				else if(shortBreakout)
					Print("Breakout SHORT at " + now);
				else
					return; // to not continue any other logic since neither have happened. efficiency
				
				// Step 2, check if the box size is valid or else skip the trade
				double minBox = IsLondonSession ? MinBoxLondon : MinBoxNewYork; // find min box required based on which session it is
				if(BoxHeight < minBox || BoxHeight > MaxBox) 
				{
					Print($"✖️ BoxHeight {BoxHeight:F2} invalid (needs {minBox:F2}–{MaxBox:F2})");
					return; // skip trade if box height is invalid
				}
				Print($"✔️ BoxHeight OK: {BoxHeight:F2}");
				
				// Step 3, confirm the EMAs
				double ema9 = EMA(Closes[0], 9)[0];
				double ema21 = EMA(Closes[0], 21)[0];
				bool emaOKLong = Close[0] > ema9 && ema9 > ema21;
				bool emaOKShort = Close[0] < ema9 && ema9 < ema21;
				
				// Skip trades if EMAs do not fit the rules
				if(longBreakout && !emaOKLong) 
				{
					Print($"✖️ EMA misaligned LONG: Close={Close[0]:F2}, 9EMA={ema9:F2}, 21EMA={ema21:F2}");
                    return;
				}
				
				if (shortBreakout && !emaOKShort)
                {
                    Print($"✖️ EMA misaligned SHORT: Close={Close[0]:F2}, 9EMA={ema9:F2}, 21EMA={ema21:F2}");
                    return;
                }
                Print("✔️ EMA alignment OK"); // EMA fit rules so continue
				
				
				// Step 4, volume filter with alert
				double volAvg = 0;
				for (int i = 1; i <=5; i++) {
					volAvg += Volume[i];
				}
				volAvg /= 5;
				double volThresh = volAvg * (IsLondonSession ? VolMultiplierLon : VolMultiplierNY);
				
				if(Volume[0] < volThresh) 
				{
					Print($"⚠️ Low volume: {Volume[0]} vs threshold {volThresh:F0}");
					Alert("LowVol", Priority.High, "Low volume breakout", "Alert2.wav", 0, Brushes.Orange, Brushes.Black);
				}
				
				else 
				{
					Print($"✔️ Volume OK: {Volume[0]} vs {volThresh:F0}");
				}
				
				
				// Step 5, provide support/resistance context alert about previous day high/low
				// Find target profit depending on if it's a long or short
				double target = longBreakout ? Close[0] + Math.Min(BoxHeight * 2, 4) : Close[0] - Math.Min(BoxHeight * 2, 4);
				
				if(longBreakout && PrevDayHigh - target <= 2)
				{
					Print($"⚠️ TP {target:F2} within 2 of yesterday’s HIGH {PrevDayHigh:F2}");
            		Alert("SRHigh", Priority.Medium, "TP near prior-day HIGH", "", 0, Brushes.Yellow, Brushes.Black);
				}
				
				else if (shortBreakout && target - PrevDayLow <= 2)
		        {
		            Print($"⚠️ TP {target:F2} within 2 of yesterday’s LOW {PrevDayLow:F2}");
		            Alert("SRLow", Priority.Medium, "TP near prior-day LOW",  "", 0, Brushes.Yellow, Brushes.Black);
		        }
		        else
				{
		            Print("✔️ Context OK");
				}
					
				
			}
			
			
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

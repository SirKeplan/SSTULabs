using System;
using System.Collections.Generic;
using UnityEngine;
namespace SSTUTools
{
	/// <summary>
	/// Generic animation module intended to be controlled by other PartModules.
	/// <para>Does not include any GUI or direct-user-interactivity; all state changes
	/// must be initiated from external sources</para>
	/// </summary>
	public class SSTUAnimateControlled : PartModule
	{
		//lookup value for external modules to find the proper animation module when multiple anim modules are in use
		[KSPField]
		public int animationID = 0;

		[KSPField]
		public String animationName = String.Empty;

		[KSPField]
		public int animationLayer = 0;

		/// <summary>
		/// The animation speed MULTIPLIER.
		/// Values higher than 1 increase default playback speed.
		/// Values between 0 and 1 will decrease playback speed (0=paused).
		/// Values below 0 have undefined results.
		/// </summary>
		[KSPField]
		public float animationSpeed = 1;

		[KSPField(isPersistant=true)]
		public String persistentState = SSTUAnimState.STOPPED_START.ToString();

		private SSTUAnimState currentAnimState = SSTUAnimState.STOPPED_START;

		private List<Action<SSTUAnimState>> onAnimStateChangeCallbacks = new List<Action<SSTUAnimState>>();

		private Animation[] anims;
			
		//Static method for use by other modules to locate a control module; reduces code duplication in animation controlling modules
		public static SSTUAnimateControlled locateAnimationController(Part part, int id, Action<SSTUAnimState> callback)
		{
			if (id < 0)
			{
				return null;
			}
			SSTUAnimateControlled[] potentialAnimators = part.GetComponents<SSTUAnimateControlled>();
			foreach(SSTUAnimateControlled ac in potentialAnimators)
			{
				if(ac.animationID == id)
				{
					ac.addCallback(callback);
					return ac;
				}
			}
			return null;
		}

		public override void OnStart (StartState state)
		{
			base.OnStart (state);
			if (!HighLogic.LoadedSceneIsFlight && !HighLogic.LoadedSceneIsEditor)
			{
				return;//should never happen, onStart not called for prefabs
			}
			if(anims==null)//means OnLoad was not called for this instance;
			{
				locateAnimation();
				restorePreviousAnimationState(currentAnimState);							
			}
		}

		public override void OnLoad (ConfigNode node)
		{
			base.OnLoad (node);
			try
			{				
				currentAnimState = (SSTUAnimState) Enum.Parse (typeof(SSTUAnimState), persistentState);
			}
			catch(Exception e)
			{
				currentAnimState = SSTUAnimState.STOPPED_START;
				persistentState = currentAnimState.ToString();
				print (e.Message);
			}
			locateAnimation();
			restorePreviousAnimationState(currentAnimState);				
		}

		public void addCallback(Action<SSTUAnimState> cb)
		{
			onAnimStateChangeCallbacks.Add (cb);
		}

		//External method to set the state; does not callback on this state change, as this is supposed to originate -from- the callback;
		//it should be aware of its own instigated state changes
		public void setToState(SSTUAnimState newState)
		{
			setAnimState (newState, false);
		}

		public SSTUAnimState getAnimationState()
		{
			return currentAnimState;
		}

		public void Update()
		{
			if (currentAnimState == SSTUAnimState.PLAYING_BACKWARD || currentAnimState == SSTUAnimState.PLAYING_FORWARD)
			{				
				bool playing = false;
				foreach (Animation a in anims)
				{
					if(a[animationName].enabled)//the animationClip/State will be enabled if it is playing; solves problems of always-active anims (thermal emissive) interfering with anim control
					{
						playing = true;
						break;
					}
				}
				//if no longer playing, set the new animation state and inform the callback of the change
				if (!playing)
				{
					SSTUAnimState newState = currentAnimState==SSTUAnimState.PLAYING_BACKWARD ? SSTUAnimState.STOPPED_START : SSTUAnimState.STOPPED_END;
					setAnimState(newState, true);
				}
			}
		}

		private void setAnimState(SSTUAnimState newState, bool callback)
		{
			switch (newState)
			{
			case SSTUAnimState.PLAYING_BACKWARD:			
			{
				setAnimSpeed(-1f);
				if(currentAnimState==SSTUAnimState.STOPPED_END)//enforce play backwards from end
				{
					setAnimTime(1f);
				}
				playAnimation();
				break;
			}
			case SSTUAnimState.PLAYING_FORWARD:
			{
				setAnimSpeed(1f);
				if(currentAnimState==SSTUAnimState.STOPPED_START)//enforce play forwards from beginning
				{
					setAnimTime(0f);
				}
				playAnimation();
				break;
			}
			case SSTUAnimState.STOPPED_END:
			{
				setAnimTime(1f);
				setAnimSpeed(1);
				playAnimation();
				break;
			}
			case SSTUAnimState.STOPPED_START:
			{
				setAnimTime(0);
				setAnimSpeed(-1);
				playAnimation();
				break;
			}
			}
			currentAnimState = newState;
			persistentState = currentAnimState.ToString();
			if (callback && onAnimStateChangeCallbacks!=null)
			{
				int len = onAnimStateChangeCallbacks.Count;
				for(int i = 0; i < len; i++)
				{
					onAnimStateChangeCallbacks[i].Invoke(currentAnimState);
				}
			}
		}
		
		private void locateAnimation()
		{
			Animation[] animsBase = part.gameObject.GetComponentsInChildren<Animation>(true);
			List<Animation> al = new List<Animation>();
			foreach(Animation a in animsBase)
			{
				AnimationClip c = a.GetClip(animationName);
				if(c!=null)
				{
					al.Add (a);
				}				
			}			
			anims = al.ToArray();
			if (anims == null || anims.Length==0)
			{
				SSTUUtils.recursePrintComponents(part.gameObject, "");
				throw new NullReferenceException("Cannot instantiate SSTUAnimateControlled with no animatons; could not locate animations for: "+animationName);
			}
			foreach (Animation a in anims)
			{
				a[animationName].layer = animationLayer;
				a[animationName].wrapMode = WrapMode.Once;
				a.wrapMode = WrapMode.Once;				
			}
		}

		private void playAnimation()
		{
			foreach(Animation a in anims)
			{
				a.Play(animationName);
			}
		}

		private void stopAnimation()
		{
			foreach(Animation a in anims)
			{
				a.Stop(animationName);
			}
		}

		private void setAnimTime(float time)
		{
			foreach(Animation a in anims)
			{
				a[animationName].normalizedTime = time;
			}
		}

		private void setAnimSpeed(float speed)
		{
			foreach(Animation a in anims)
			{
				a[animationName].speed = speed * animationSpeed;
			}
		}

		private void restorePreviousAnimationState(SSTUAnimState state)
		{
			if (state == SSTUAnimState.PLAYING_BACKWARD)
			{
				state = SSTUAnimState.STOPPED_START;
			}
			else if (state == SSTUAnimState.PLAYING_FORWARD)
			{
				state = SSTUAnimState.STOPPED_END;
			}
			setAnimState (state, false);
		}
	}
}

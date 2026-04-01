using System;
using System.Collections;
using UnityEngine;

namespace FourWinsTikTok.Gameplay
{
    public class TurnTimer : MonoBehaviour
    {
        public event Action<float> OnTick;
        public event Action OnCompleted;

        private Coroutine _countdownRoutine;

        public bool IsRunning { get; private set; }
        public float RemainingTime { get; private set; }

        public void StartCountdown(float durationSeconds)
        {
            if (!this)
            {
                return;
            }

            CancelCountdown();

            if (durationSeconds <= 0f)
            {
                RemainingTime = 0f;
                OnTick?.Invoke(RemainingTime);
                OnCompleted?.Invoke();
                return;
            }

            _countdownRoutine = StartCoroutine(RunCountdown(durationSeconds));
        }

        public void CancelCountdown()
        {
            if (!this)
            {
                return;
            }

            if (_countdownRoutine != null)
            {
                StopCoroutine(_countdownRoutine);
                _countdownRoutine = null;
            }

            IsRunning = false;
            RemainingTime = 0f;
        }

        private IEnumerator RunCountdown(float durationSeconds)
        {
            IsRunning = true;
            RemainingTime = durationSeconds;
            float endTime = Time.realtimeSinceStartup + durationSeconds;
            OnTick?.Invoke(RemainingTime);

            while (RemainingTime > 0f)
            {
                RemainingTime = Mathf.Max(0f, endTime - Time.realtimeSinceStartup);

                OnTick?.Invoke(RemainingTime);
                yield return null;
            }

            IsRunning = false;
            _countdownRoutine = null;
            OnCompleted?.Invoke();
        }
    }
}

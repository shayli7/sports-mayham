using System;
using UnityEngine;

namespace SportsMayhem.Calibration
{
    /// <summary>
    /// Body measurements taken during calibration (T-pose), saved on the device.
    /// Defaults are average adult values, used until the player calibrates.
    /// </summary>
    [Serializable]
    public sealed class PlayerCalibration
    {
        private const string PrefsKey = "SportsMayhem.PlayerCalibration";

        /// <summary>True once the player has completed a calibration.</summary>
        public bool isCalibrated;

        /// <summary>Meters.</summary>
        public float shoulderWidth = 0.38f;
        public float upperArmLength = 0.28f;
        public float forearmLength = 0.26f;

        /// <summary>Where the shoulder midpoint was on screen (0..1, mirror view) when calibrating.</summary>
        public Vector2 screenCenter = new Vector2(0.5f, 0.55f);

        /// <summary>Shoulder width as a fraction of image width when calibrating (a distance-to-camera reference).</summary>
        public float screenShoulderWidth = 0.2f;

        public float ArmLength => upperArmLength + forearmLength;

        private static PlayerCalibration _current;

        /// <summary>The active calibration (loaded from the device on first use).</summary>
        public static PlayerCalibration Current => _current ??= Load();

        public static event Action<PlayerCalibration> Changed;

        public static void Save(PlayerCalibration calibration)
        {
            _current = calibration;
            PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(calibration));
            PlayerPrefs.Save();
            Changed?.Invoke(calibration);
        }

        public static void Clear()
        {
            PlayerPrefs.DeleteKey(PrefsKey);
            _current = new PlayerCalibration();
            Changed?.Invoke(_current);
        }

        private static PlayerCalibration Load()
        {
            var json = PlayerPrefs.GetString(PrefsKey, "");
            if (string.IsNullOrEmpty(json)) return new PlayerCalibration();
            try
            {
                return JsonUtility.FromJson<PlayerCalibration>(json) ?? new PlayerCalibration();
            }
            catch (ArgumentException)
            {
                return new PlayerCalibration();
            }
        }
    }
}

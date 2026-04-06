using System;

namespace Spine.Unity {
	/// <summary>Minimal stub for BlendModeMaterials (full implementation removed).</summary>
	[Serializable]
	public class BlendModeMaterials {
		public void ApplyMaterials (SkeletonData skeletonData) { }
	}

	/// <summary>Minimal stub for BlendModeMaterialsAsset (full implementation removed).</summary>
	public class BlendModeMaterialsAsset : SkeletonDataModifierAsset {
		public override void Apply (SkeletonData skeletonData) { }
	}
}

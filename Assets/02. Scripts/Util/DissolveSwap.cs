using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 렌더러 묶음을 <b>소멸 머티리얼로 갈아끼웠다 되돌린다</b>.
///
/// <para>적 본체(<c>EnemyView</c>)와 시체(<c>CorpseView</c>)가 같은 일을 하고,
/// 둘 다 <b>풀에 반납</b>되므로 원복을 빠뜨리면 다음 대여가 타다 만 채로 나온다 —
/// 그 함정을 한 곳에만 두려고 여기 있다.</para>
///
/// <para>원래 머티리얼(툰 셰이더)에는 <c>_Dissolve</c>가 없다. 그래서 진행을 미는 것만으로는
/// 아무 일도 일어나지 않는다 — <b>갈아끼우는 이 단계가 있어야 소멸이 화면에 보인다</b>.</para>
/// </summary>
public sealed class DissolveSwap
{
    private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

    private readonly List<Renderer> swapped = new List<Renderer>();
    private readonly List<Material[]> originals = new List<Material[]>();

    public bool Active => swapped.Count > 0;

    /// <summary>
    /// 갈아끼우고, 각 렌더러의 원래 텍스처를 프로퍼티 블록으로 넘겨준다 —
    /// 소멸 머티리얼은 하나를 공유하지만 겉모습은 적마다 유지된다.
    /// </summary>
    public void Begin(IList<Renderer> renderers, Material dissolveMaterial, MaterialPropertyBlock block)
    {
        if (Active || renderers == null || dissolveMaterial == null || block == null) return;

        foreach (var r in renderers)
        {
            if (r == null || !r.enabled) continue;

            var before = r.sharedMaterials;
            var after = new Material[before.Length];
            for (int i = 0; i < after.Length; i++) after[i] = dissolveMaterial;

            // ponytail: 서브메쉬마다 텍스처가 다르면 첫 장으로 통일된다. 적 모델이 실제로 갈리면 그때 나눈다.
            var main = before.Length > 0 && before[0] != null ? before[0].mainTexture : null;

            swapped.Add(r);
            originals.Add(before);
            r.sharedMaterials = after;

            r.GetPropertyBlock(block);
            if (main != null) block.SetTexture(BaseMapId, main);
            r.SetPropertyBlock(block);
        }
    }

    /// <summary>
    /// 풀 반납 직전 원복. <b>빠뜨리면 다음 대여가 소멸 머티리얼을 입은 채 나온다.</b>
    ///
    /// <para>프로퍼티 블록도 같이 비운다 — 넘겨 둔 텍스처·진행값이 원래 머티리얼의
    /// 같은 이름 프로퍼티를 덮을 수 있다.</para>
    /// </summary>
    public void Restore()
    {
        for (int i = 0; i < swapped.Count; i++)
        {
            if (swapped[i] == null) continue;

            swapped[i].sharedMaterials = originals[i];
            swapped[i].SetPropertyBlock(null);
        }

        swapped.Clear();
        originals.Clear();
    }
}

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace StoryProps.EditorTools
{
    /// <summary>
    /// 스테이지별 배경 배치표를 현재 씬에 세운다.
    /// ⚠ 배치표의 진실의 원천은 이제 이 파일의 좌표 테이블이다(설계 문서였던
    ///   docs/Story/Plan_StageMaps.md 는 삭제됐다). 무대 의미는 docs/Story/Story_Overview.md §5 참조.
    ///
    /// ⚠ 무대 원(반경 8m, 5스테이지는 3.5m) 안은 반드시 평면이다 —
    ///   이동이 transform 대입이라 경사면을 못 탄다(CLAUDE.md §11-2).
    ///   경사·계단은 전부 무대 원 밖 배경으로만 놓는다.
    ///
    /// 먼저 Tools/Story Props/Extract Prefabs 를 한 번 실행해야 낱개 프리팹이 생긴다.
    /// </summary>
    public static class StageLayoutBuilder
    {
        const string StoryPrefabs = "Assets/03. Prefabs/StoryProps";
        const string CityPrefabs =
            "Assets/99. External Assets/Pandazole_Lowpoly_Asset_Bundle/Pandazole City Town Pack/Prefabs";
        const string DojoModel = "Assets/06. Models/Stage/Dojo_Stage.fbx";

        struct P
        {
            public string Name;
            public Vector3 Pos;
            public float Yaw;
            public Vector3 Scale;

            public P(string name, float x, float y, float z, float yaw = 0f)
            {
                Name = name; Pos = new Vector3(x, y, z); Yaw = yaw; Scale = Vector3.one;
            }

            public P(string name, float x, float y, float z, float yaw, Vector3 scale)
            {
                Name = name; Pos = new Vector3(x, y, z); Yaw = yaw; Scale = scale;
            }
        }

        // ─────────────────────────────────────────── 공용 바닥 (§0-1)

        /// <summary>야외 스테이지 바닥: 도로 타일 3×3 = 30×30m. 타일 하나가 10×10m.</summary>
        static IEnumerable<P> RoadGrid()
        {
            yield return new P("Env_Road_Cross_01", 0, 0, 0);
            yield return new P("Env_Road_Straight_01", 0, 0, 10);
            yield return new P("Env_Road_Straight_01", 0, 0, -10);
            yield return new P("Env_Road_Straight_01", 10, 0, 0, 90);
            yield return new P("Env_Road_Straight_01", -10, 0, 0, 90);
            yield return new P("Env_Road_Cornor_01", 10, 0, 10, 0);
            yield return new P("Env_Road_Cornor_01", -10, 0, 10, 90);
            yield return new P("Env_Road_Cornor_01", -10, 0, -10, 180);
            yield return new P("Env_Road_Cornor_01", 10, 0, -10, 270);
        }

        // ─────────────────────────────────────────── 스테이지별 배치표

        static IEnumerable<P> Stage1()
        {
            foreach (var p in RoadGrid()) yield return p;

            yield return new P("Env_CommercialBuilding_01", -22, 0, 6, 90);
            yield return new P("Env_CommercialBuilding_02", 22, 0, -4, -90);
            yield return new P("Env_CommercialBuilding_03", -20, 0, -18, 90);
            yield return new P("Env_ResidentBuilding_01", -18, 0, 22, 180);
            yield return new P("Env_ResidentBuilding_02", 18, 0, 22, 180);
            yield return new P("Env_ResidentBuilding_03", -18, 0, -24, 0);
            yield return new P("Env_ResidentBuilding_04", 18, 0, -24, 0);

            // 사람은 없는데 기계는 돌아간다 — 균열 ①
            yield return new P("Prop_VendingMachine", -9.5f, 0, 4.2f, 90);
            yield return new P("Prop_VendingMachine", 11.0f, 0, -7.5f, -90);

            // 간판은 전부 켜져 있다
            yield return new P("Prop_Sign_Vertical", -12.5f, 3.2f, 6.0f, 90);
            yield return new P("Prop_Sign_Vertical", -12.5f, 3.2f, -9.0f, 90);
            yield return new P("Prop_Sign_Vertical", 12.5f, 3.2f, 2.0f, -90);
            yield return new P("Prop_Sign_Vertical", 12.5f, 3.2f, -14.0f, -90);
            yield return new P("Prop_Sign_Vertical", -12.5f, 3.2f, 15.0f, 90);
            yield return new P("Prop_Sign_Vertical", 12.5f, 3.2f, 14.0f, -90);
            yield return new P("Prop_Sign_Horizontal", -12.2f, 3.6f, 0.0f, 90);
            yield return new P("Prop_Sign_Horizontal", 12.2f, 3.6f, -3.0f, -90);
            yield return new P("Prop_Sign_Horizontal", -12.2f, 3.6f, -14.0f, 90);
            yield return new P("Prop_Sign_Horizontal", 12.2f, 3.6f, 9.0f, -90);
            yield return new P("Prop_Noren", -11.8f, 1.9f, 3.0f, 90);
            yield return new P("Prop_Noren", 11.8f, 1.9f, -6.0f, -90);
            yield return new P("Prop_Noren", -11.8f, 1.9f, -11.0f, 90);
            yield return new P("Prop_Lantern", -11.6f, 2.5f, 1.4f, 0);
            yield return new P("Prop_Lantern", -11.6f, 2.5f, 4.6f, 0);
            yield return new P("Prop_Lantern", 11.6f, 2.5f, -4.4f, 0);
            yield return new P("Prop_Lantern", 11.6f, 2.5f, -7.6f, 0);
            yield return new P("Prop_Sign_Standing", -8.6f, 0, 1.5f, 24);
            yield return new P("Prop_Sign_Standing", 9.2f, 0, -2.0f, -37);

            // 육지장 1벌 — 카메라 기본 방위, 무대 원 밖
            yield return new P("Prop_JizoRow", 0, 0, 10.5f, 180);
            // ⚠ NPC 는 만들지 않는다. 그것이 균열이다.
        }

        static IEnumerable<P> Stage2()
        {
            foreach (var p in RoadGrid()) yield return p;

            // 물이 없는 강 — 20m
            for (int i = 0; i < 4; i++)
                yield return new P("Prop_DryCulvert", -11, 0, -10 + i * 5);

            for (int i = 0; i < 4; i++)
                yield return new P("Prop_RetainingWall", 13.5f, 0, -10 + i * 5);

            for (int i = 0; i < 3; i++)
                yield return new P("Prop_Guardrail", -9.2f, 0, -8 + i * 4);

            yield return new P("Prop_Stairs", 13.0f, 0, 9.5f, -90);

            // ⚠ 유일한 경사 — 무대 원 밖 배경 전용
            yield return new P("Prop_HillRoadSlope", 0, 0, 19);
            yield return new P("Prop_HillRoadSlope", 0, 1.05f, 24);

            yield return new P("Env_ResidentBuilding_01", -19, 0, 13, 90);
            yield return new P("Env_ResidentBuilding_02", -19, 0, -13, 90);
            yield return new P("Env_ResidentBuilding_05", 19, 0, 13, -90);
            yield return new P("Env_ResidentBuilding_06", 19, 0, -13, -90);
            yield return new P("Prop_Tree_01", 15.5f, 2.5f, -6);
            yield return new P("Prop_Tree_02", 16.2f, 2.5f, 2);
            yield return new P("Prop_Tree_03", 14.8f, 2.5f, 7);
            yield return new P("Prop_Tree_04", 16.8f, 2.5f, -12);
            yield return new P("Prop_Tree_05", 15.0f, 2.5f, 14);
        }

        static IEnumerable<P> Stage3()
        {
            foreach (var p in RoadGrid()) yield return p;

            // 1스테이지와 같은 건물, 위치만 어긋난다
            yield return new P("Env_CommercialBuilding_01", 22, 0, 6, -90);
            yield return new P("Env_CommercialBuilding_01", -22, 0, -14, 90);   // ⚠ 같은 건물이 두 번
            yield return new P("Env_CommercialBuilding_02", -20, 0, 18, 90);
            yield return new P("Env_CommercialBuilding_03", 20, 0, -20, -90);

            // ⚠ 같은 간판이 두 번 — 완전히 같은 프리팹·회전·높이여야 한다
            yield return new P("Prop_Sign_Vertical", -12.5f, 3.2f, 6.0f, 90);
            yield return new P("Prop_Sign_Vertical", 12.5f, 3.2f, 6.0f, -90);
            yield return new P("Prop_Sign_Vertical", -12.5f, 3.2f, -9.0f, 90);
            yield return new P("Prop_Sign_Vertical", 12.5f, 3.2f, -9.0f, -90);
            yield return new P("Prop_Sign_Vertical", -12.5f, 3.2f, 14.0f, 90);
            yield return new P("Prop_Sign_Vertical", -12.5f, 3.2f, -16.0f, 90);
            yield return new P("Prop_Sign_Vertical", 12.5f, 3.2f, 14.0f, -90);
            yield return new P("Prop_Sign_Vertical", 12.5f, 3.2f, -16.0f, -90);

            // 육지장 네 벌 — 4방위, 완전히 같은 배치
            yield return new P("Prop_JizoRow", 0, 0, 10.5f, 180);
            yield return new P("Prop_JizoRow", 10.5f, 0, 0, -90);
            yield return new P("Prop_JizoRow", 0, 0, -10.5f, 0);
            yield return new P("Prop_JizoRow", -10.5f, 0, 0, 90);

            yield return new P("Prop_VendingMachine", -9.5f, 0, 4.2f, 90);
            yield return new P("Prop_VendingMachine", 9.5f, 0, 4.2f, -90);
        }

        static IEnumerable<P> Stage4()
        {
            // 벽이 지나치게 높다 — Y 스케일 1.4 (≈11.5m)
            yield return new P("Dojo_Stage", 0, 0, 0, 0, new Vector3(1f, 1.4f, 1f));

            // 쌓았다 무너진 돌탑 — 5스테이지 회수의 좌표 원본
            yield return new P("Prop_Cairn_A", -8.6f, 0, 3.2f, 12);
            yield return new P("Prop_Cairn_B", 8.9f, 0, -4.1f, -30);
            yield return new P("Prop_CairnFallen_A", -9.4f, 0, -2.6f, 45);
            yield return new P("Prop_CairnFallen_B", 9.2f, 0, 5.8f, -18);
            yield return new P("Prop_CairnFallen_C", 0, 0, -9.8f, 70);

            // ⚠ 슬립은 꺼 둔다 — 여기서는 「아홉 칸 격자」이지 출석표가 아니다
            yield return new P("Prop_GridBoard", 0, 1.5f, 16.2f, 180);

            yield return new P("Prop_Shelf_Intact", -14.5f, 0, 10.0f, 90);
            yield return new P("Prop_Shelf_Intact", 14.5f, 0, 10.0f, -90);
            yield return new P("Prop_KatanaRack", 0, 1.4f, -16.2f, 0);
        }

        static IEnumerable<P> Stage5()
        {
            yield return new P("Stage5_StorageRoom", 0, 0, 0);
            // 존재만 계속 화면에 있고 아무도 만지지 않는다
            yield return new P("Prop_KatanaRack", 5.3f, 1.40f, 1.2f, -90);
            // ⚠ 4스테이지 Cairn_A 와 같은 상대 좌표 — "그것이 이것이었다"
            yield return new P("Prop_CollapsedShelf", -2.6f, 0, 1.0f, 20);
            yield return new P("Prop_Shelf_Intact", 3.4f, 0, 3.4f, 180);
            // ⚠ 슬립 ON — 출석표로 회수된다
            yield return new P("Prop_GridBoard", 0, 1.5f, 3.9f, 180);
            yield return new P("Prop_GridBoard_Slips", 0, 1.5f, 3.9f, 180);
        }

        static IEnumerable<P> EndingTrue()
        {
            yield return new P("Prop_IVStand", 0.9f, 0, 0.4f);
            yield return new P("Prop_Chair", -1.1f, 0, 0.2f, 200);
            yield return new P("Prop_WallChart", 0, 1.45f, 1.9f, 180);
            // 손잡이에 파편 2와 같은 무늬 테이프
            yield return new P("Prop_ShinaiBag", -1.5f, 0, -0.3f, 28);
        }

        // ─────────────────────────────────────────── 메뉴

        [MenuItem("Tools/Story Props/Build Stage Layout/Stage 1 - 상점가", priority = 320)]
        static void B1() => Build("Stage1_ShoppingStreet", Stage1(), 8f);

        [MenuItem("Tools/Story Props/Build Stage Layout/Stage 2 - 언덕길", priority = 321)]
        static void B2() => Build("Stage2_HillRoad", Stage2(), 8f);

        [MenuItem("Tools/Story Props/Build Stage Layout/Stage 3 - 어긋난 상점가", priority = 322)]
        static void B3() => Build("Stage3_SkewedStreet", Stage3(), 8f);

        [MenuItem("Tools/Story Props/Build Stage Layout/Stage 4 - 도장 실내", priority = 323)]
        static void B4() => Build("Stage4_DojoInterior", Stage4(), 8f);

        [MenuItem("Tools/Story Props/Build Stage Layout/Stage 5 - 도구실", priority = 324)]
        static void B5() => Build("Stage5_StorageRoom_Layout", Stage5(), 3.5f);

        [MenuItem("Tools/Story Props/Build Stage Layout/Ending - 트루(병실)", priority = 325)]
        static void BT() => Build("EndingTrue_HospitalRoom", EndingTrue(), 0f);

        static void Build(string rootName, IEnumerable<P> placements, float stageRadius)
        {
            var existing = GameObject.Find(rootName);
            if (existing != null &&
                !EditorUtility.DisplayDialog("Story Props",
                    $"'{rootName}' 이(가) 이미 씬에 있다. 지우고 다시 세울까?", "다시 세운다", "취소"))
                return;

            if (existing != null) Undo.DestroyObjectImmediate(existing);

            var root = new GameObject(rootName);
            Undo.RegisterCreatedObjectUndo(root, "Build Stage Layout");

            // 무대 중심 마커 — EnemyDirector.arenaCenter 가 이것을 가리킨다(⚠ 플레이어가 아니다)
            if (stageRadius > 0f)
            {
                var stage = new GameObject($"Stage (radius {stageRadius}m)");
                stage.transform.SetParent(root.transform, false);
            }

            int placed = 0;
            var missing = new HashSet<string>();

            foreach (var p in placements)
            {
                var prefab = Resolve(p.Name);
                if (prefab == null) { missing.Add(p.Name); continue; }

                // ⚠ 프리팹 자신의 로컬 트랜스폼을 건드리지 않는다 — FBX 축 보정이 거기 들어 있을 수 있다.
                //   대신 컨테이너를 만들어 그것을 배치한다. 임포트 설정이 어떻든 결과가 같다.
                var holder = new GameObject(p.Name);
                holder.transform.SetParent(root.transform, false);
                holder.transform.localPosition = p.Pos;
                holder.transform.localRotation = Quaternion.Euler(0f, p.Yaw, 0f);
                holder.transform.localScale = p.Scale;

                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, holder.transform);
                if (go == null)
                {
                    missing.Add(p.Name);
                    Object.DestroyImmediate(holder);
                    continue;
                }
                placed++;
            }

            Selection.activeGameObject = root;

            string msg = $"[StoryProps] '{rootName}' 배치 완료 — {placed}개.";
            if (missing.Count > 0)
                msg += $"\n못 찾은 프리팹 {missing.Count}종: {string.Join(", ", missing)}" +
                       "\n→ Tools/Story Props/Extract Prefabs 를 먼저 실행했는지 확인.";
            Debug.Log(msg);
        }

        static GameObject Resolve(string name)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>($"{StoryPrefabs}/{name}.prefab");
            if (go != null) return go;

            go = AssetDatabase.LoadAssetAtPath<GameObject>($"{CityPrefabs}/{name}.prefab");
            if (go != null) return go;

            if (name == "Dojo_Stage") return AssetDatabase.LoadAssetAtPath<GameObject>(DojoModel);

            return null;
        }
    }
}

using System.Runtime.CompilerServices;

// 절단 코어의 내부 표현(Vertex, BoneWeightUtil, WorkMesh)을 테스트에서 직접 검증하기 위함.
// 공개 API로 승격시키면 쓰지도 않을 표면이 늘어난다 — 테스트에만 열어 준다.
[assembly: InternalsVisibleTo("Slice.Tests")]

using System;
using System.Collections.Generic;

namespace Folderss.Models
{
    /// <summary>폴더 비교에서 한 항목(상대 경로)의 판정.</summary>
    public enum CompareStatus
    {
        Same,
        Different,
        LeftOnly,
        RightOnly,
        /// <summary>한쪽은 폴더, 한쪽은 파일(또는 한쪽만 링크).</summary>
        TypeMismatch,
        /// <summary>접근 거부·IO 오류로 비교하지 못함.</summary>
        Unknown
    }

    /// <summary>
    /// 폴더 비교 옵션. 기본값이 권장값이다 — 비교 창의 비교 버튼은 이 값으로 바로 실행하고 ▾에서만 바꾼다.
    /// </summary>
    public sealed class DirectoryCompareOptions
    {
        /// <summary>크기가 같은 파일 쌍을 SHA-256으로 판정한다(수정 시각 무시). 느리다.</summary>
        public bool UseContentHash { get; set; }

        /// <summary>
        /// 크기가 같고 수정 시각만 허용 오차를 넘게 다른 파일 쌍을 바이트 비교로 다시 판정한다.
        /// checkout·압축 해제로 시각만 바뀐 파일을 같음으로 본다. 그 파일들을 읽어야 해 기본 끔(가벼운 비교).
        /// <see cref="UseContentHash"/>가 켜져 있으면 의미가 없다.
        /// </summary>
        public bool VerifyContentWhenTimeDiffers { get; set; }

        /// <summary>수정 시각 허용 오차. FAT·네트워크 드라이브의 2초 해상도 때문에 기본 2초.</summary>
        public TimeSpan TimestampTolerance { get; set; } = TimeSpan.FromSeconds(2);

        /// <summary>.gitignore/.folderssignore 규칙에 걸리는 항목을 뺀다.</summary>
        public bool ApplyIgnoreRules { get; set; } = true;

        public bool Recursive { get; set; } = true;

        /// <summary>하위 폴더 최대 깊이. 0 = 무제한.</summary>
        public int MaxDepth { get; set; }

        /// <summary>숨김·시스템 파일도 비교한다.</summary>
        public bool IncludeHidden { get; set; }

        // 아래는 판정이 아니라 비교 창 표시·동작 옵션이다(바꿔도 다시 스캔하지 않는다). 처음 값은 가장 안전한 쪽.

        /// <summary>다른 파일을 수정 시각으로 최신/오래됨 색으로 칠한다. 시각만 바뀐 파일을 최신으로 잘못 보일 수 있어 기본 끔.</summary>
        public bool DirectionColors { get; set; }

        public bool ShowFolderTree { get; set; }

        public bool ShowPreview { get; set; }

        /// <summary>같은 항목을 숨긴다(차이만 보기). 툴바 체크와 같은 값.</summary>
        public bool HideSame { get; set; }

        /// <summary>이동할 폴더의 하위 파일이 이 수 이상이면 확인한다. 0이면 폴더 이동은 늘 확인.</summary>
        public int MoveConfirmThreshold { get; set; } = 100;

        public DirectoryCompareOptions Clone() => (DirectoryCompareOptions)MemberwiseClone();
    }

    public sealed class CompareEntry
    {
        /// <summary>비교 루트 기준 경로(구분자는 OS 기본, Windows는 '\').</summary>
        public string RelativePath { get; set; }
        public string Name { get; set; }
        /// <summary>루트 직속이 0.</summary>
        public int Depth { get; set; }
        public bool IsDirectory { get; set; }
        public CompareStatus Status { get; set; }
        public bool LeftExists { get; set; }
        public bool RightExists { get; set; }
        public long? LeftSize { get; set; }
        public DateTime? LeftModified { get; set; }
        public long? RightSize { get; set; }
        public DateTime? RightModified { get; set; }
        /// <summary>툴팁용 판정 사유("크기 1,024 ≠ 2,048", "해시 불일치", "접근 거부" 등). 같음이면 보통 null.</summary>
        public string Reason { get; set; }

        /// <summary>양쪽에 다 있는 폴더 — 상태는 자식 판정을 모은 값이라 집계 개수에서 뺀다.</summary>
        public bool IsAggregateFolder => IsDirectory && LeftExists && RightExists && Status != CompareStatus.TypeMismatch;
    }

    public sealed class CompareResult
    {
        public string LeftRoot { get; set; }
        public string RightRoot { get; set; }
        public DirectoryCompareOptions Options { get; set; }

        /// <summary>화면 순서(깊이 우선, 각 폴더 안에서 폴더 먼저·이름순). 폴더 항목이 자식보다 앞에 온다.</summary>
        public List<CompareEntry> Entries { get; } = new List<CompareEntry>();

        /// <summary>취소되면 false — 지금까지 판정한 항목만 들어 있다.</summary>
        public bool IsComplete { get; set; }

        public int SameCount { get; set; }
        public int DifferentCount { get; set; }
        public int LeftOnlyCount { get; set; }
        public int RightOnlyCount { get; set; }
        public int TypeMismatchCount { get; set; }
        public int UnknownCount { get; set; }

        public bool HasDifferences => DifferentCount + LeftOnlyCount + RightOnlyCount + TypeMismatchCount > 0;
    }
}

import { BlockType } from './course.model';

export interface CourseProgress {
  courseId: string;
  courseTitle: string;
  enrolledAt: Date;
  lastActivityAt: Date;
  currentBlockId?: string;
  completedBlocksCount: number;
  totalBlocksCount: number;
  progressPercentage: number;
  chapters: ChapterProgress[];
}

export interface ChapterProgress {
  chapterId: string;
  title: string;
  orderIndex: number;
  subchapters: SubchapterProgress[];
}

export interface SubchapterProgress {
  subchapterId: string;
  title: string;
  orderIndex: number;
  blocks: BlockProgress[];
}

export interface BlockProgress {
  blockId: string;
  title: string;
  type: BlockType;
  orderIndex: number;
  isCompleted: boolean;
  completedAt?: Date;
}

// Structure without progress (for non-enrolled users)
export interface CourseStructure {
  courseId: string;
  courseTitle: string;
  courseDescription: string;
  totalBlocksCount: number;
  chapters: ChapterStructure[];
}

export interface ChapterStructure {
  chapterId: string;
  title: string;
  orderIndex: number;
  subchapters: SubchapterStructure[];
}

export interface SubchapterStructure {
  subchapterId: string;
  title: string;
  orderIndex: number;
  blocksCount: number;
}

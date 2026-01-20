export enum CourseStatus {
  Draft = 0,
  Published = 1
}

export enum BlockType {
  Theory = 0,
  Video = 1,
  Quiz = 2,
  Problem = 3
}

export interface Course {
  id: string;
  title: string;
  description: string;
  status: CourseStatus;
  instructorId: string;
  instructorName: string;
  createdAt: Date;
  publishedAt?: Date;
  chaptersCount: number;
  totalBlocks: number;
}

export interface CreateCourseRequest {
  title: string;
  description: string;
}

export interface UpdateCourseRequest {
  title: string;
  description: string;
}

export interface Chapter {
  id: string;
  title: string;
  orderIndex: number;
  courseId: string;
  subchaptersCount: number;
}

export interface CreateChapterRequest {
  title: string;
}

export interface Subchapter {
  id: string;
  title: string;
  orderIndex: number;
  chapterId: string;
  blocksCount: number;
}

export interface CreateSubchapterRequest {
  title: string;
}

export interface Block {
  id: string;
  title: string;
  type: BlockType;
  orderIndex: number;
  subchapterId: string;
  theoryContent?: TheoryContent;
  videoContent?: VideoContent;
  quiz?: Quiz;
  problem?: Problem;
}

export interface TheoryContent {
  content: string;
}

export interface VideoContent {
  videoUrl: string;
  videoId: string;
  durationSeconds?: number;
}

export interface Quiz {
  id: string;
  questions: QuizQuestion[];
}

export interface QuizQuestion {
  id: string;
  content: string;
  type: string; // "SingleChoice" | "MultipleChoice" | "TrueFalse"
  points: number;
  explanation?: string;
  orderIndex: number;
  answers: QuizAnswer[];
}

export interface QuizAnswer {
  id: string;
  text: string;
  orderIndex: number;
  isCorrect?: boolean; // Only populated for instructors/admins editing quizzes
}

export interface Problem {
  id: string;
  description: string;
  difficulty: 'Easy' | 'Medium' | 'Hard';
  tags?: Tag[];
  starterCodes?: StarterCode[];
}

export interface Tag {
  id: string;
  name: string;
}

export interface StarterCode {
  id: string;
  code: string;
  languageId: string;
  languageName: string;
}

export interface Enrollment {
  courseId: string;
  message: string;
  enrolledAt: Date;
}

export interface EnrolledCourse {
  courseId: string;
  courseTitle: string;
  courseDescription: string;
  instructorName: string;
  enrolledAt: Date;
  lastActivityAt: Date;
  currentBlockId?: string;
  completedBlocksCount: number;
  totalBlocksCount: number;
  progressPercentage: number;
}

export interface EnrollmentStatus {
  isEnrolled: boolean;
}

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
  url: string;
  duration?: number;
}

export interface Quiz {
  id: string;
  questions: QuizQuestion[];
}

export interface QuizQuestion {
  id: string;
  questionText: string;
  answers: QuizAnswer[];
}

export interface QuizAnswer {
  id: string;
  answerText: string;
  isCorrect: boolean;
}

export interface Problem {
  id: string;
  description: string;
  difficulty: 'Easy' | 'Medium' | 'Hard';
  timeLimit: number;
  memoryLimit: number;
}

export interface Enrollment {
  courseId: string;
  message: string;
  enrolledAt: Date;
}

export interface EnrolledCourse {
  id: string;
  title: string;
  description: string;
  instructorName: string;
  enrolledAt: Date;
  currentBlockId?: string;
  progressPercentage: number;
}

export interface EnrollmentStatus {
  isEnrolled: boolean;
}

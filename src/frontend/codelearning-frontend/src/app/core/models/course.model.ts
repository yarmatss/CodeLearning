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

export interface RenameChapterRequest {
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

export interface RenameSubchapterRequest {
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
  type: string; // "SingleChoice" | "MultipleChoice" | "TrueFalse" (serialized as a string by the API)
  points: number;
  // Null for students until they submit the quiz
  explanation?: string | null;
  orderIndex: number;
  answers: QuizAnswer[];
}

export interface QuizAnswer {
  id: string;
  text: string;
  orderIndex: number;
  isCorrect?: boolean; // Only sent to the course owner; students learn it from the quiz result
}

export interface Problem {
  id: string;
  title: string;
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
  // Enrollments survive unpublishing; the content is unavailable while the course is a draft
  courseStatus: CourseStatus;
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

/** Course response of POST /api/courses/{id}/publish and /unpublish. */
export interface CourseStatusChangeResponse {
  message: string;
  course: Course;
}

/** GET /api/courses/my/stats */
export interface TeacherStats {
  publishedCourses: number;
  draftCourses: number;
  totalStudents: number;
}

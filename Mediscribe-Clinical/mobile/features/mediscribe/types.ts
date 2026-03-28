export type UserRole = 'doctor' | 'staff';

export type SessionStatus = 'pending' | 'in_review' | 'completed';

export type TranscriptSpeaker = 'doctor' | 'patient';

export type MedicationRecommendationStatus = 'suggested' | 'reviewed' | 'approved' | 'rejected';

export interface ClinicUser {
  id: string;
  name: string;
  email: string;
  role: UserRole;
}

export interface PatientProfile {
  id: string;
  fullName: string;
  age: number;
  sex: 'female' | 'male' | 'other';
  allergies: string[];
  ineffectiveMedications: string[];
}

export interface TranscriptEntry {
  id: string;
  speaker: TranscriptSpeaker;
  text: string;
  timestamp: string;
  confidence: number;
}

export interface SoapNote {
  subjective: string;
  objective: string;
  assessment: string;
  plan: string;
  approvedAt?: string;
  approvedBy?: string;
}

export interface MedicationRecommendation {
  id: string;
  name: string;
  dosage: string;
  frequency: string;
  ageGroup: string;
  status: MedicationRecommendationStatus;
  safetyNotes: string;
  rationale: string;
  requiresDoctorValidation: boolean;
  validatedByDoctor: boolean;
}

export interface PostVisitSummary {
  id: string;
  content: string;
  generatedAt: string;
  editedAt?: string;
}

export interface SessionAuditEvent {
  id: string;
  actorId: string;
  action: string;
  timestamp: string;
}

export interface ClinicalSession {
  id: string;
  patient: PatientProfile;
  ownerId: string;
  ownerName: string;
  ownerRole: UserRole;
  startedAt: string;
  status: SessionStatus;
  transcript: TranscriptEntry[];
  soapNote: SoapNote;
  medicationRecommendations: MedicationRecommendation[];
  postVisitSummary?: PostVisitSummary;
  auditEvents: SessionAuditEvent[];
}

export interface NewSessionPayload {
  patientName: string;
  age: number;
  sex: 'female' | 'male' | 'other';
  allergies: string[];
  ineffectiveMedications: string[];
  transcriptDoctorText: string;
  transcriptPatientText: string;
}

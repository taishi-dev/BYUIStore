from flask import Flask, render_template, request, redirect, url_for, session, jsonify, flash
from flask_sqlalchemy import SQLAlchemy
from datetime import datetime
import requests as http_requests
import os

app = Flask(__name__)
app.secret_key = "byui_verba_collect_secret_2026"
app.config["SQLALCHEMY_DATABASE_URI"] = "sqlite:///verba_collect.db"
app.config["SQLALCHEMY_TRACK_MODIFICATIONS"] = False

db = SQLAlchemy(app)

# ---------------------------------------------------------------------------
# Database Models
# ---------------------------------------------------------------------------

class User(db.Model):
    id = db.Column(db.Integer, primary_key=True)
    username = db.Column(db.String(80), unique=True, nullable=False)
    password = db.Column(db.String(120), nullable=False)
    # roles: professor, office_manager, bookstore_staff
    role = db.Column(db.String(50), nullable=False)
    full_name = db.Column(db.String(150), nullable=False)

class CourseRequest(db.Model):
    id = db.Column(db.Integer, primary_key=True)
    submitter_id = db.Column(db.Integer, db.ForeignKey("user.id"), nullable=False)
    submitter = db.relationship("User", backref="requests")
    course_name = db.Column(db.String(200), nullable=False)
    course_number = db.Column(db.String(50), nullable=False)
    semester = db.Column(db.String(50), nullable=False)
    # status: pending_verification, verified, approved, rejected
    status = db.Column(db.String(50), default="pending_verification")
    submitted_at = db.Column(db.DateTime, default=datetime.utcnow)
    verified_by_id = db.Column(db.Integer, db.ForeignKey("user.id"), nullable=True)
    verified_by = db.relationship("User", foreign_keys=[verified_by_id])
    verified_at = db.Column(db.DateTime, nullable=True)
    approved_by_id = db.Column(db.Integer, db.ForeignKey("user.id"), nullable=True)
    approved_by = db.relationship("User", foreign_keys=[approved_by_id])
    approved_at = db.Column(db.DateTime, nullable=True)
    rejection_note = db.Column(db.String(500), nullable=True)
    items = db.relationship("RequestItem", backref="course_request", cascade="all, delete-orphan")

class RequestItem(db.Model):
    id = db.Column(db.Integer, primary_key=True)
    request_id = db.Column(db.Integer, db.ForeignKey("course_request.id"), nullable=False)
    item_type = db.Column(db.String(20), nullable=False)  # "book" or "supply"
    title = db.Column(db.String(300), nullable=True)
    author = db.Column(db.String(200), nullable=True)
    isbn = db.Column(db.String(20), nullable=True)
    publisher = db.Column(db.String(200), nullable=True)
    edition = db.Column(db.String(50), nullable=True)
    supply_description = db.Column(db.String(300), nullable=True)
    quantity = db.Column(db.Integer, default=1)
    required = db.Column(db.Boolean, default=True)
    notes = db.Column(db.String(500), nullable=True)

# ---------------------------------------------------------------------------
# Seed demo users
# ---------------------------------------------------------------------------

def seed_demo_data():
    if User.query.count() == 0:
        users = [
            User(username="prof_smith",    password="byui1234", role="professor",        full_name="Prof. John Smith"),
            User(username="coord_lee",     password="byui1234", role="professor",        full_name="Coord. Sarah Lee"),
            User(username="manager_jones", password="byui1234", role="office_manager",   full_name="Manager Amy Jones"),
            User(username="staff_brown",   password="byui1234", role="bookstore_staff",  full_name="Staff Tom Brown"),
        ]
        for u in users:
            db.session.add(u)
        db.session.commit()

# ---------------------------------------------------------------------------
# Authentication helpers
# ---------------------------------------------------------------------------

def current_user():
    if "user_id" in session:
        return User.query.get(session["user_id"])
    return None

def login_required(f):
    from functools import wraps
    @wraps(f)
    def decorated(*args, **kwargs):
        if not current_user():
            flash("Please log in first.", "warning")
            return redirect(url_for("login"))
        return f(*args, **kwargs)
    return decorated

def role_required(*roles):
    from functools import wraps
    def decorator(f):
        @wraps(f)
        def decorated(*args, **kwargs):
            user = current_user()
            if not user or user.role not in roles:
                flash("You do not have permission to access that page.", "danger")
                return redirect(url_for("dashboard"))
            return f(*args, **kwargs)
        return decorated
    return decorator

# ---------------------------------------------------------------------------
# Routes – Auth
# ---------------------------------------------------------------------------

@app.route("/")
def index():
    return redirect(url_for("login"))

@app.route("/login", methods=["GET", "POST"])
def login():
    if request.method == "POST":
        username = request.form.get("username", "").strip()
        password = request.form.get("password", "")
        user = User.query.filter_by(username=username, password=password).first()
        if user:
            session["user_id"] = user.id
            flash(f"Welcome, {user.full_name}!", "success")
            return redirect(url_for("dashboard"))
        flash("Invalid username or password.", "danger")
    return render_template("login.html")

@app.route("/logout")
def logout():
    session.clear()
    flash("You have been logged out.", "info")
    return redirect(url_for("login"))

# ---------------------------------------------------------------------------
# Routes – Dashboard
# ---------------------------------------------------------------------------

@app.route("/dashboard")
@login_required
def dashboard():
    user = current_user()
    if user.role in ("professor",):
        my_requests = CourseRequest.query.filter_by(submitter_id=user.id).order_by(CourseRequest.submitted_at.desc()).all()
        return render_template("dashboard_professor.html", user=user, requests=my_requests)
    elif user.role == "office_manager":
        pending = CourseRequest.query.filter_by(status="pending_verification").order_by(CourseRequest.submitted_at.desc()).all()
        all_req  = CourseRequest.query.order_by(CourseRequest.submitted_at.desc()).all()
        my_requests = CourseRequest.query.filter_by(submitter_id=user.id).order_by(CourseRequest.submitted_at.desc()).all()
        return render_template("dashboard_office_manager.html", user=user,
                               pending=pending, all_requests=all_req, my_requests=my_requests)
    elif user.role == "bookstore_staff":
        verified = CourseRequest.query.filter_by(status="verified").order_by(CourseRequest.submitted_at.desc()).all()
        all_req  = CourseRequest.query.order_by(CourseRequest.submitted_at.desc()).all()
        return render_template("dashboard_bookstore_staff.html", user=user,
                               verified=verified, all_requests=all_req)
    return redirect(url_for("login"))

# ---------------------------------------------------------------------------
# Routes – Submit Request (professors AND office managers)
# ---------------------------------------------------------------------------

@app.route("/submit", methods=["GET", "POST"])
@login_required
@role_required("professor", "office_manager")
def submit_request():
    user = current_user()
    if request.method == "POST":
        course_name   = request.form.get("course_name", "").strip()
        course_number = request.form.get("course_number", "").strip()
        semester      = request.form.get("semester", "").strip()

        if not course_name or not course_number or not semester:
            flash("Please fill in all course fields.", "danger")
            return redirect(url_for("submit_request"))

        new_req = CourseRequest(
            submitter_id=user.id,
            course_name=course_name,
            course_number=course_number,
            semester=semester,
        )
        db.session.add(new_req)
        db.session.flush()  # get new_req.id before committing

        # Parse dynamic item rows
        item_types = request.form.getlist("item_type[]")
        for i, itype in enumerate(item_types):
            if itype == "book":
                item = RequestItem(
                    request_id=new_req.id,
                    item_type="book",
                    title=request.form.getlist("title[]")[i],
                    author=request.form.getlist("author[]")[i],
                    isbn=request.form.getlist("isbn[]")[i],
                    publisher=request.form.getlist("publisher[]")[i],
                    edition=request.form.getlist("edition[]")[i],
                    quantity=int(request.form.getlist("quantity[]")[i] or 1),
                    required=("required[]" in request.form and request.form.getlist("required[]")[i] == "on"),
                    notes=request.form.getlist("notes[]")[i],
                )
            else:
                item = RequestItem(
                    request_id=new_req.id,
                    item_type="supply",
                    supply_description=request.form.getlist("supply_description[]")[i],
                    quantity=int(request.form.getlist("quantity[]")[i] or 1),
                    required=("required[]" in request.form and request.form.getlist("required[]")[i] == "on"),
                    notes=request.form.getlist("notes[]")[i],
                )
            db.session.add(item)

        db.session.commit()
        flash("Request submitted successfully!", "success")
        return redirect(url_for("dashboard"))

    return render_template("submit_request.html", user=user)

# ---------------------------------------------------------------------------
# Routes – ISBN Lookup (AJAX)
# ---------------------------------------------------------------------------

@app.route("/api/isbn_lookup")
@login_required
def isbn_lookup():
    """Search Open Library by title/author and return book metadata including ISBN."""
    title  = request.args.get("title", "").strip()
    author = request.args.get("author", "").strip()
    if not title:
        return jsonify({"error": "Please enter a book title."}), 400

    query_parts = []
    if title:
        query_parts.append(f"title={http_requests.utils.quote(title)}")
    if author:
        query_parts.append(f"author={http_requests.utils.quote(author)}")

    url = f"https://openlibrary.org/search.json?{'&'.join(query_parts)}&limit=8&fields=title,author_name,isbn,publisher,edition_count,first_publish_year"
    try:
        resp = http_requests.get(url, timeout=8)
        data = resp.json()
    except Exception as e:
        return jsonify({"error": f"Could not reach Open Library: {str(e)}"}), 500

    results = []
    for doc in data.get("docs", []):
        isbn_list = doc.get("isbn", [])
        # Prefer 13-digit ISBNs
        isbn13 = next((x for x in isbn_list if len(x) == 13), None)
        isbn10 = next((x for x in isbn_list if len(x) == 10), None)
        best_isbn = isbn13 or isbn10 or (isbn_list[0] if isbn_list else "")
        results.append({
            "title": doc.get("title", ""),
            "author": ", ".join(doc.get("author_name", [])),
            "isbn": best_isbn,
            "publisher": (doc.get("publisher") or [""])[0],
            "edition": str(doc.get("edition_count", "")),
            "year": doc.get("first_publish_year", ""),
        })
    return jsonify({"results": results})

# ---------------------------------------------------------------------------
# Routes – Verify (Office Manager & Bookstore Staff)
# ---------------------------------------------------------------------------

@app.route("/verify/<int:req_id>", methods=["GET", "POST"])
@login_required
@role_required("office_manager", "bookstore_staff")
def verify_request(req_id):
    user = current_user()
    req  = CourseRequest.query.get_or_404(req_id)

    if request.method == "POST":
        action = request.form.get("action")
        if action == "verify":
            req.status = "verified"
            req.verified_by_id = user.id
            req.verified_at = datetime.utcnow()
            db.session.commit()
            flash("Request marked as verified.", "success")
        elif action == "reject":
            req.status = "rejected"
            req.rejection_note = request.form.get("rejection_note", "")
            db.session.commit()
            flash("Request rejected.", "warning")
        return redirect(url_for("dashboard"))

    return render_template("verify_request.html", user=user, req=req)

# ---------------------------------------------------------------------------
# Routes – Approve (Bookstore Staff only)
# ---------------------------------------------------------------------------

@app.route("/approve/<int:req_id>", methods=["GET", "POST"])
@login_required
@role_required("bookstore_staff")
def approve_request(req_id):
    user = current_user()
    req  = CourseRequest.query.get_or_404(req_id)

    if request.method == "POST":
        action = request.form.get("action")
        if action == "approve":
            req.status = "approved"
            req.approved_by_id = user.id
            req.approved_at = datetime.utcnow()
            db.session.commit()
            flash("Request approved!", "success")
        elif action == "reject":
            req.status = "rejected"
            req.rejection_note = request.form.get("rejection_note", "")
            db.session.commit()
            flash("Request rejected.", "warning")
        return redirect(url_for("dashboard"))

    return render_template("approve_request.html", user=user, req=req)

# ---------------------------------------------------------------------------
# Routes – View single request detail
# ---------------------------------------------------------------------------

@app.route("/request/<int:req_id>")
@login_required
def view_request(req_id):
    user = current_user()
    req  = CourseRequest.query.get_or_404(req_id)
    return render_template("view_request.html", user=user, req=req)

# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

if __name__ == "__main__":
    with app.app_context():
        db.create_all()
        seed_demo_data()
    app.run(debug=True, port=5000)
